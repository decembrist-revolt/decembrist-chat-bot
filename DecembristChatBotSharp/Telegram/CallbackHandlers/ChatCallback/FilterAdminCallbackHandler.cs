using Lamar;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;

[Singleton]
public class FilterAdminCallbackHandler : IChatCallbackHandler
{
    public const string PrefixKey = "FilterAdmin";
    public string Prefix => PrefixKey;

    public Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        throw new NotImplementedException();
    }
}

public enum FilterAdminDecision
{
    Bun,
    UnBun
}