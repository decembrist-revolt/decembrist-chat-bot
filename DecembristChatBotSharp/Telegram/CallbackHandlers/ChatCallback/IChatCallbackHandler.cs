namespace DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;

public interface IChatCallbackHandler
{
    public string Prefix { get; }
    Task<Unit> Do(CallbackQueryParameters queryParameters);
}