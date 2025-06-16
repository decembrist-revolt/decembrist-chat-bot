using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using LanguageExt.Common;
using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

public enum Result
{
    JustMessage,
    Captcha
}

[Singleton]
public class CaptchaHandler(
    AppConfig appConfig,
    BotClient botClient,
    NewMemberRepository db,
    WhiteListRepository whiteListRepository,
    MessageAssistance messageAssistance,
    CancellationTokenSource cancelToken
)
{
    private readonly CaptchaConfig _captchaConfig = appConfig.CaptchaConfig;

    public async Task<Result> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        var payload = parameters.Payload;

        var maybe = await db.FindNewMember((telegramId, chatId)).Match(
            identity,
            ex => OnNewMemberNotFound(ex, telegramId, chatId));
        if (maybe.IsNone) return Result.JustMessage;

        var newMember = maybe.ValueUnsafe();

        var tryCaptcha = TryAsync(IsCaptchaPassed(payload)
            ? OnCaptchaPassed(telegramId, chatId, messageId, newMember)
            : OnCaptchaFailed(telegramId, chatId, messageId, newMember));

        return await tryCaptcha.IfFail(ex =>
                Log.Error(ex, "Captcha handler failed for user {0} in chat {1}", telegramId, chatId))
            .Map(_ => Result.Captcha);
    }

    private OptionNone OnNewMemberNotFound(Error error, long telegramId, long chatId)
    {
        Log.Error(error, "New member not found for user {0} in chat {1}", telegramId, chatId);
        return None;
    }

    private bool IsCaptchaPassed(IMessagePayload payload) =>
        payload is TextPayload { Text: var text } &&
        string.Equals(appConfig.CaptchaConfig.CaptchaAnswer, text, StringComparison.CurrentCultureIgnoreCase);

    private async Task<Unit> OnCaptchaPassed(
        long telegramId,
        long chatId,
        int messageId,
        NewMember newMember)
    {
        var joinMessage = string.Format(_captchaConfig.JoinText, newMember.Username);
        var result = await db.RemoveNewMember((telegramId, chatId))
            .Map(removed => OnNewMemberRemoved(removed, telegramId, chatId))
            .MapAsync(_ => Task.WhenAll(
                whiteListRepository.AddWhiteListMember(new WhiteListMember((telegramId, chatId))),
                botClient.DeleteMessages(chatId, [messageId, newMember.WelcomeMessageId], cancelToken.Token),
                botClient.SendMessage(chatId, joinMessage, cancellationToken: cancelToken.Token)
            ).UnitTask());

        return result.Match(
            _ => Log.Information(
                "Deleted welcome message and captcha message for user {0} in chat {1}", telegramId, chatId),
            ex => Log.Error(ex,
                "Failed to delete welcome message and captcha message for user {0} in chat {1}", telegramId, chatId)
        );
    }

    private async Task<Unit> OnCaptchaFailed(
        long telegramId,
        long chatId,
        int messageId,
        NewMember newMember)
    {
        Log.Information("User {0} failed captcha in chat {1}", telegramId, chatId);

        var trySend = await SendFailedMessage(telegramId, chatId, messageId, newMember);

        return trySend.Match(
            isBan =>
            {
                var status = isBan ? "Banned" : "Failed captcha";
                Log.Information("User {0} in chat {1} - {2}", telegramId, chatId, status);
            },
            ex => Log.Error(ex, "Failed to handle failed captcha for user {0} in chat {1}", telegramId, chatId)
        );
    }

    /// <returns>true if user was banned</returns>
    private TryAsync<bool> SendFailedMessage(
        long telegramId,
        long chatId,
        int messageId,
        NewMember newMember)
    {
        var welcomeMessageId = newMember.WelcomeMessageId;
        var retryCount = newMember.CaptchaRetryCount;

        var tryUpdate = Try(() => db.UpdateNewMemberRetries(newMember.Id, retryCount + 1))
            .Map(_ => OnNewMemberUpdated(telegramId, chatId));

        var captchaTask = retryCount switch
        {
            _ when retryCount < _captchaConfig.CaptchaRetryCount => EditRetries(chatId, newMember, welcomeMessageId),
            _ when retryCount % 5 == 0 => SendCaptchaMessage(chatId, newMember.Username),
            _ when retryCount == _captchaConfig.CaptchaRetryCount =>
                messageAssistance.DeleteCommandMessage(chatId, welcomeMessageId, nameof(CaptchaHandler)),
            _ => Task.FromResult(unit),
        };

        var deleteTask = messageAssistance.DeleteCommandMessage(chatId, messageId, nameof(CaptchaHandler));
        return tryUpdate
            .MapAsync(_ => Array(captchaTask, deleteTask).WhenAll())
            .Map(_ => false);
    }

    private Task<Unit> SendCaptchaMessage(long chatId, string username)
    {
        var message = string.Format(_captchaConfig.CaptchaRequestAgainText, username, _captchaConfig.CaptchaAnswer);
        var expireAt = DateTime.UtcNow.AddSeconds(_captchaConfig.CaptchaTimeSeconds);
        return messageAssistance.SendCommandResponse(chatId, message, nameof(CaptchaHandler), expireAt);
    }

    private Task<Unit> EditRetries(long chatId, NewMember newMember, int welcomeMessageId)
    {
        var message =
            string.Format(_captchaConfig.WelcomeMessage, newMember.Username, _captchaConfig.CaptchaTimeSeconds)
            + "\n\n" + string.Format(_captchaConfig.CaptchaFailedText,
                _captchaConfig.CaptchaRetryCount - newMember.CaptchaRetryCount);

        return messageAssistance.EditMessageAndLog(chatId, welcomeMessageId, message, nameof(CaptchaHandler));
    }

    private Unit OnNewMemberRemoved(bool result, long telegramId, long chatId)
    {
        if (result)
            Log.Information("Deleted new member {0} in chat {1}", telegramId, chatId);
        else
            Log.Error("Failed to delete new member {0} in chat {1}", telegramId, chatId);

        return unit;
    }

    private Unit OnNewMemberUpdated(long telegramId, long chatId)
    {
        Log.Information("Updated new member {0} in chat {1}", telegramId, chatId);

        return unit;
    }
}