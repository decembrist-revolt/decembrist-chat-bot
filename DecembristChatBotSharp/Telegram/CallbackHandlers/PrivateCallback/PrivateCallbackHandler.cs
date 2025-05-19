using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using Lamar;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;

[Singleton]
public class PrivateCallbackHandler(
    MessageAssistance messageAssistance,
    Lazy<IList<IPrivateCallbackHandler>> callbackHandler)
{
    public async Task<Unit> Do(CallbackQueryParameters callbackQueryParameters)
    {
        var prefix = callbackQueryParameters.Prefix;
        var telegramId = callbackQueryParameters.TelegramId;
        var maybeHandler = callbackHandler.Value
            .Find(x => x.Prefix.Equals(prefix, StringComparison.CurrentCultureIgnoreCase));

        return await maybeHandler.MatchAsync(
            handler => handler.Do(callbackQueryParameters),
            () => messageAssistance.SendCommandResponse(telegramId, "OK", nameof(PrivateCallbackHandler)));
    }
}