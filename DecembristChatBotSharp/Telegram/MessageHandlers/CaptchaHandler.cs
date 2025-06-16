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
    NewMemberRepository newMemberRepository,
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

        var maybe = await newMemberRepository.FindNewMember((telegramId, chatId)).Match(
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
        var result = await newMemberRepository.RemoveNewMember((telegramId, chatId))
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
    private async Task<Unit> SendFailedMessage(
        long telegramId,
        long chatId,
        int messageId,
        NewMember newMember)
    {
        var retryCount = newMember.CaptchaRetryCount;
        if (retryCount % 5 == 0) return await SendCaptchaMessage(chatId, newMember);

        var captchaTask = retryCount % 5 == 0 ? SendCaptchaMessage(chatId, newMember) : Task.FromResult(unit);

        var tryUpdate = Try(() =>
                newMemberRepository.AddMemberItem(newMember with { CaptchaRetryCount = retryCount + 1 }))
            .Map(_ => OnNewMemberUpdated(telegramId, chatId));

        var deleteTask = messageAssistance.DeleteCommandMessage(chatId, messageId, nameof(CaptchaHandler));
        return await Array(captchaTask, deleteTask ).WhenAll();
    }

    private async Task<Unit> SendCaptchaMessage(long chatId, NewMember newMember)
    {
        var username = newMember.Username;
        var welcomeMessageId = newMember.WelcomeMessageId;
        var text = string.Format(_captchaConfig.CaptchaRequestAgainText, username, _captchaConfig.CaptchaAnswer);

        await messageAssistance.DeleteCommandMessage(chatId, welcomeMessageId, nameof(CaptchaHandler));
        var maybeMessageId = Option<int>.None;
        await botClient.SendMessageAndLog(chatId, text,
            message =>
            {
                maybeMessageId = message.MessageId;
                Log.Information("Sent captcha message to chat {0}", chatId);
            },
            ex => Log.Error(ex, "Failed to send captcha message to chat {0}", chatId), cancelToken.Token);
        if (maybeMessageId.IsSome)
        {
            await newMemberRepository.AddMemberItem(newMember with
            {
                CaptchaRetryCount = newMember.CaptchaRetryCount + 1, WelcomeMessageId = maybeMessageId.ValueUnsafe()
            });
        }
        else
        {
            await newMemberRepository.AddMemberItem(newMember with
            {
                CaptchaRetryCount = newMember.CaptchaRetryCount + 1
            });
        }

        return unit;
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