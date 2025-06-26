using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using DecembristChatBotSharp.Telegram.LoreHandlers;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;

[Singleton]
public class FilterCallbackHandler(
    MessageAssistance messageAssistance,
    AppConfig appConfig,
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
            None: () => messageAssistance.SendCommandResponse(chatId, "OK", nameof(FilterCallbackHandler)),
            Some: async parameters =>
            {
                if (!callbackService.HasChatIdKey(parameters, out var targetChatId)) return unit;

                return filterSuffix switch
                {
                    FilterSuffix.Create => await SendRequestFilterRecord(targetChatId, telegramId),
                    FilterSuffix.Delete => await SendRequestDelete(targetChatId, telegramId),
                    _ => throw new ArgumentOutOfRangeException(nameof(suffix), suffix, null)
                };
            });
        return await Array(taskResult, messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix)).WhenAll();
    }

    private Task<Unit> SendRequestDelete(long targetChatId, long chatId)
    {
        var message = string.Format(appConfig.FilterConfig.DeleteRequest,
            GetFilterTag(FilterRecordHandler.DeleteSuffix, targetChatId));
        return messageAssistance.SendCommandResponse(chatId, message, nameof(FilterCallbackHandler),
            replyMarkup: new ForceReplyMarkup());
    }

    private Task<Unit> SendRequestFilterRecord(long targetChatId, long chatId)
    {
        var message = string.Format(appConfig.FilterConfig.CreateRequest,
            GetFilterTag(FilterRecordHandler.RecordSuffix, targetChatId));
        return messageAssistance.SendCommandResponse(chatId, message, nameof(FilterCallbackHandler),
            replyMarkup: new ForceReplyMarkup());
    }

    public static string GetFilterTag(string suffix, long targetChatId, string key = "") =>
        $"\n{FilterRecordHandler.Tag}{suffix}:{key}:{targetChatId}";
}

public enum FilterSuffix
{
    Create,
    Delete
}