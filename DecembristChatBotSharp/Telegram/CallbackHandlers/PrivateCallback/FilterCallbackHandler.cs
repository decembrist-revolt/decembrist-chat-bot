using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;

[Singleton]
public class FilterCallbackHandler(
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    ChatConfigService chatConfigService,
    CallbackService callbackService
) : IPrivateCallbackHandler
{
    public const string PrefixKey = "Filter";
    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, chatId, telegramId, messageId, queryId, maybeParameters) = queryParameters;
        if (!Enum.TryParse(suffix, true, out FilterSuffix filterSuffix)) return unit;

        var taskResult = maybeParameters.MatchAsync(
            None: () => messageAssistance.SendMessageExpired(chatId, "OK", nameof(FilterCallbackHandler)),
            Some: async parameters =>
            {
                if (!callbackService.HasChatIdKey(parameters, out var targetChatId) &&
                    !await messageAssistance.IsAllowedChat(targetChatId)) return unit;

                var maybeFilterConfig = await chatConfigService.GetConfig(targetChatId, config => config.FilterConfig);
                if (!maybeFilterConfig.TryGetSome(out var filterConfig))
                {
                    return chatConfigService.LogNonExistConfig(unit, nameof(FilterConfig), Prefix);
                }

                return filterSuffix switch
                {
                    _ when !await adminUserRepository.IsAdmin(new CompositeId(telegramId, targetChatId)) =>
                        await messageAssistance.SendAdminOnlyMessage(telegramId, telegramId),
                    FilterSuffix.Create => await SendRequestFilterRecord(targetChatId, telegramId, filterConfig),
                    FilterSuffix.Delete => await SendRequestDelete(targetChatId, telegramId, filterConfig),
                    _ => throw new ArgumentOutOfRangeException(nameof(suffix), suffix, null)
                };
            });
        return await Array(taskResult, messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix)).WhenAll();
    }

    private async Task<Unit> SendRequestDelete(long targetChatId, long chatId, FilterConfig filterConfig)
    {
        var message = string.Format(filterConfig.DeleteRequest,
            GetFilterTag(FilterRecordHandler.DeleteSuffix, targetChatId));
        return await messageAssistance.SendMessageExpired(chatId, message, nameof(FilterCallbackHandler),
            replyMarkup: new ForceReplyMarkup());
    }

    private async Task<Unit> SendRequestFilterRecord(long targetChatId, long chatId, FilterConfig filterConfig)
    {
        var message = string.Format(filterConfig.CreateRequest,
            GetFilterTag(FilterRecordHandler.RecordSuffix, targetChatId));
        return await messageAssistance.SendMessageExpired(chatId, message, nameof(FilterCallbackHandler),
            replyMarkup: new ForceReplyMarkup());
    }

    private static string GetFilterTag(string suffix, long targetChatId) =>
        $"\n{FilterRecordHandler.Tag}{suffix}:{targetChatId}";
}

public enum FilterSuffix
{
    Create,
    Delete
}