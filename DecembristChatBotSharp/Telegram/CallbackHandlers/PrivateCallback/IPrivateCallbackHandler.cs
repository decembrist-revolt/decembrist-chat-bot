namespace DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;

public interface IPrivateCallbackHandler
{
    public string Prefix { get; }
    Task<Unit> Do(CallbackQueryParameters queryParameters);
}

public record CallbackQueryParameters(
    string Prefix,
    string Suffix,
    long ChatId,
    long TelegramId,
    int MessageId,
    string queryId,
    Option<Map<string, string>> Parameters);