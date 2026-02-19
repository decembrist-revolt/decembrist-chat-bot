using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;

[Singleton]
public class FilterCaptchaCallbackHandler(
    BanService banService,
    AdminUserRepository adminUserRepository,
    WhiteListRepository whiteListRepository,
    MessageAssistance messageAssistance,
    CallbackService callbackService,
    FilterRestrictUserRepository filterRestrictUserRepository,
    AppConfig appConfig)
    : IChatCallbackHandler
{
    public const string PrefixKey = "FilterAdmin";
    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, chatId, telegramId, messageId, queryId, maybeParameters) = queryParameters;
        if (!Enum.TryParse(suffix, true, out FilterAdminDecision decision)) return unit;
        if (!await adminUserRepository.IsAdmin(new CompositeId(telegramId, chatId)))
            return await SendNotAccess(queryId, chatId);

        return await maybeParameters.Map(async x =>
        {
            if (!callbackService.TryGetUserIdKey(x, out var filterUserId)) return unit;
            var banTask = decision switch
            {
                FilterAdminDecision.Ban => BanFilterUser(chatId, filterUserId),
                FilterAdminDecision.UnBan => UnBanFilterUser(chatId, filterUserId),
                _ => throw new ArgumentOutOfRangeException()
            };
            return await Array(banTask, messageAssistance.DeleteCommandMessage(chatId, messageId, Prefix)).WhenAll();
        }).IfNone(() => Task.FromResult(unit));
    }

    private async Task<Unit> BanFilterUser(long chatId, long telegramId)
    {
        Log.Information("Ban user {0} in chat {1} by admin decision", telegramId, chatId);
        await filterRestrictUserRepository.DeleteUser(new CompositeId(telegramId, chatId));
        return await banService.BanChatMember(chatId, telegramId);
    }

    private Task<Unit> UnBanFilterUser(long chatId, long telegramId)
    {
        Log.Information("Unban user {0} in chat {1} by admin decision", telegramId, chatId);
        return Array(
            filterRestrictUserRepository.DeleteUser(new CompositeId(telegramId, chatId)).ToUnit(),
            banService.UnRestrictChatMember(chatId, telegramId),
            whiteListRepository.AddWhiteListMember(new WhiteListMember(new CompositeId(telegramId, chatId))).ToUnit()
        ).WhenAll();
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