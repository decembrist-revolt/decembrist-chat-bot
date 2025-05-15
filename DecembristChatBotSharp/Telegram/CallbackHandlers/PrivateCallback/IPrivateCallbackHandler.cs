using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;

public interface IPrivateCallbackHandler
{
    public string Prefix { get; }
    Task<Unit> Do(CallbackQueryParameters queryParameters);
}
