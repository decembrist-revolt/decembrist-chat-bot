using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;

[Singleton]
public class FilterAdminCallbackHandler(
    BanService banService,
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    AppConfig appConfig)
    : IChatCallbackHandler
{
    public const string PrefixKey = "FilterAdmin";
    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, chatId, telegramId, messageId, queryId, _) = queryParameters;
        if (!Enum.TryParse(suffix, true, out FilterAdminDecision decision)) return unit;
        if (await adminUserRepository.IsAdmin(new CompositeId(telegramId, chatId)))
            return await SendNotAccess(queryId, chatId);

        return decision switch
        {
            FilterAdminDecision.Ban => await BanFilterUser(chatId, telegramId, messageId),
            FilterAdminDecision.UnBan => await UnBanFilterUser(chatId, telegramId, messageId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private async Task<Unit> BanFilterUser(long chatId, long telegramId, int messageId)
    {
        await banService.BanChatMember(chatId, telegramId);
        return await messageAssistance.DeleteCommandMessage(chatId, messageId, Prefix);
    }

    private async Task<Unit> UnBanFilterUser(long chatId, long telegramId, int messageId)
    {
        await banService.UnbanChatMember(chatId, telegramId);
        return await messageAssistance.DeleteCommandMessage(chatId, messageId, Prefix);
    }

    private async Task<Unit> SendNotAccess(string queryId, long chatId)
    {
        var message = appConfig.CommandAssistanceConfig.AdminOnlyMessage;
        return await messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix, message);
    }
}

public enum FilterAdminDecision
{
    Ban,
    UnBan
}