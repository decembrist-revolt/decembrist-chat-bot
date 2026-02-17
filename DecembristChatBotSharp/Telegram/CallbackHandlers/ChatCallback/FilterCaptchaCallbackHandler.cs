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
                FilterAdminDecision.Ban => BanFilterUser(chatId, filterUserId, messageId),
                FilterAdminDecision.UnBan => UnBanFilterUser(chatId, filterUserId, messageId),
                _ => throw new ArgumentOutOfRangeException()
            };
            return await Array(banTask, messageAssistance.DeleteCommandMessage(chatId, messageId, Prefix)).WhenAll();
        }).IfNone(() => Task.FromResult(unit));
    }

    private Task<Unit> BanFilterUser(long chatId, long telegramId, int messageId)
    {
        Log.Information("Ban user {0} in chat {1} by admin decision", telegramId, chatId);
        return banService.BanChatMember(chatId, telegramId);
    }

    private Task<Unit> UnBanFilterUser(long chatId, long telegramId, int messageId)
    {
        Log.Information("Unban user {0} in chat {1} by admin decision", telegramId, chatId);
        return Array(banService.UnRestrictChatMember(chatId, telegramId),
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