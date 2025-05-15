using DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;
using Lamar;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;

[Singleton]
public class ChatCallbackHandler(Lazy<IList<IChatCallbackHandler>> callbackHandler)
{
    public async Task<Unit> Do(CallbackQueryParameters callbackQueryParameters)
    {
        var prefix = callbackQueryParameters.Prefix;
        var maybeHandler = callbackHandler.Value
            .Find(x => x.Prefix.Equals(prefix, StringComparison.CurrentCultureIgnoreCase));

        return await maybeHandler.MatchAsync(
            handler => handler.Do(callbackQueryParameters),
            () => unit);
    }
}