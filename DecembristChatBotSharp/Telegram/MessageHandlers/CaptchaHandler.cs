using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
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

public class CaptchaHandler(
    AppConfig appConfig,
    BotClient botClient,
    NewMemberRepository db,
    CancellationTokenSource cancelToken
)
{
    public async Task<Result> Do(ChatMessageHandlerParams parameters)
    {
        var telegramId = parameters.TelegramId;
        var chatId = parameters.ChatId;
        var messageId = parameters.MessageId;
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
        string.Equals(appConfig.CaptchaAnswer, text, StringComparison.CurrentCultureIgnoreCase);

    private async Task<Unit> OnCaptchaPassed(
        long telegramId,
        long chatId,
        int messageId,
        NewMember newMember)
    {
        var joinMessage = string.Format(appConfig.JoinText, newMember.Username);
        var result = await db.RemoveNewMember((telegramId, chatId))
            .Map(removed => OnNewMemberRemoved(removed, telegramId, chatId))
            .MapAsync(_ => Task.WhenAll(
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
        var retryCount = newMember.CaptchaRetryCount + 1;
        var retryRemain = appConfig.CaptchaRetryCount - newMember.CaptchaRetryCount;

        var isBan = retryRemain == 0;
        if (isBan)
        {
            return TryAsync(ClearUser)
                .Bind(_ => db.RemoveNewMember(newMember.Id))
                .Map(removed => OnNewMemberRemoved(removed, telegramId, chatId))
                .Map(_ => true);
        }

        var tryUpdate = Try(() => db.UpdateNewMemberRetries(newMember.Id, retryCount))
            .Map(_ => OnNewMemberUpdated(telegramId, chatId));

        return tryUpdate.MapAsync(_ => EditRetries()).Map(_ => false);

        string GetNewWelcomeText() =>
            string.Format(appConfig.WelcomeMessage, newMember.Username, appConfig.CaptchaTimeSeconds)
            + "\n\n"
            + string.Format(appConfig.CaptchaFailedText, retryRemain);

        Task<Unit> ClearUser() => Task.WhenAll(
            botClient.DeleteMessages(chatId, [messageId, welcomeMessageId], cancelToken.Token),
            botClient.BanChatMember(chatId, telegramId, DateTime.UtcNow, false, cancelToken.Token)
        ).UnitTask();

        Task<Unit> EditRetries() => Task.WhenAll(
            botClient.DeleteMessage(chatId, messageId, cancelToken.Token),
            botClient.EditMessageText(chatId, welcomeMessageId, GetNewWelcomeText(),
                cancellationToken: cancelToken.Token)
        ).UnitTask();
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