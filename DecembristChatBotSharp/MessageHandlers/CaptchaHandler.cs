using DecembristChatBotSharp.Entity;
using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.MessageHandlers;

public readonly struct CaptchaHandlerParams(
    string text,
    int messageId,
    long telegramId,
    long chatId
)
{
    public string Text => text;
    public int MessageId => messageId;
    public long TelegramId => telegramId;
    public long ChatId => chatId;
}

public class CaptchaHandler(
    AppConfig appConfig,
    BotClient botClient,
    Database db)
{
    private readonly NewMember EMPTY_NEW_MEMBER = new(0, "", 0, 0, DateTime.MinValue);
    
    public async Task<Unit> Do(
        CaptchaHandlerParams parameters,
        CancellationToken cancelToken)
    {
        var telegramId = parameters.TelegramId;
        var chatId = parameters.ChatId;
        var messageId = parameters.MessageId;
        var text = parameters.Text;

        var maybe = Try(db.GetNewMember(telegramId, chatId))
            .Match(identity, Option<NewMember>.None);
        if (maybe.IsNone) return unit;
        
        var newMember = maybe.ValueUnsafe();

        var tryCaptcha = TryAsync(text == appConfig.CaptchaAnswer
            ? OnCaptchaPassed(telegramId, chatId, messageId, newMember, cancelToken)
            : OnCaptchaFailed(telegramId, chatId, messageId, newMember, cancelToken));

        await tryCaptcha.IfFail(ex =>
            Log.Error(ex, "Captcha handler failed for user {0} in chat {1}", telegramId, chatId));

        return Try(db.RemoveNewMember(telegramId, chatId)).Match(
            Succ: result => OnRemoveMember(result, telegramId, chatId),
            Fail: ex =>
            {
                Log.Error(ex, "Failed to remove user {0} from new members in chat {1}", telegramId, chatId);
                return unit;
            });
    }

    private Unit OnRemoveMember(bool result, long telegramId, long chatId)
    {
        if (result)
        {
            Log.Information("User {0} removed from new members in chat {1}", telegramId, chatId);
        }
        else
        {
            Log.Error(
                "Failed to remove user {0} from new members in chat {1}", telegramId, chatId);
        }

        return unit;
    }

    private async Task<Unit> OnCaptchaPassed(
        long telegramId,
        long chatId,
        int messageId,
        NewMember newMember,
        CancellationToken cancelToken)
    {
        var joinMessage = string.Format(appConfig.JoinText, newMember.Username);
        var result = TryAsync(Task.WhenAll(
            botClient.DeleteMessages(chatId, [messageId, newMember.WelcomeMessageId], cancelToken),
            botClient.SendMessage(chatId, joinMessage, cancellationToken: cancelToken)
        ));

        return await result.Match(
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
        NewMember newMember,
        CancellationToken cancelToken)
    {
        Log.Information("User {0} failed captcha in chat {1}", telegramId, chatId);

        int[] messages = [messageId, newMember.WelcomeMessageId];

        var result = TryAsync(Task.WhenAll(
            botClient.DeleteMessages(chatId, messages, cancelToken),
            botClient.BanChatMember(chatId, telegramId, DateTime.UtcNow.AddSeconds(0), false, cancelToken)
        ));

        return await result.Match(
            _ => Log.Information("Cleared user {0} in chat {1}", telegramId, chatId),
            ex => Log.Error(ex, "Failed to clear user {0} in chat {1}", telegramId, chatId)
        );
    }
}