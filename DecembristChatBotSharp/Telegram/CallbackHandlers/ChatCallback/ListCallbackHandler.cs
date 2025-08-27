using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Service.Buttons;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;

[Singleton]
public class ListCallbackHandler(
    AppConfig appConfig,
    CallbackRepository callbackRepository,
    MessageAssistance messageAssistance,
    ListButtons listButtons,
    ListService listService) : IChatCallbackHandler
{
    public const string PrefixKey = "ListChat";

    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, chatId, telegramId, messageId, queryId, maybeParameters) = queryParameters;
        if (!Enum.TryParse(suffix, true, out ListType listType)) return unit;

        return await SwitchList(telegramId, chatId, messageId, queryId, listType, maybeParameters.ValueUnsafe());
    }

    private async Task<Unit> SwitchList(
        long presserId, long chatId, int messageId, string queryId, ListType listType, Map<string, string> parameters)
    {
        if (!listService.IsContainIndex(parameters, out var currentOffset)) return unit;

        var id = new CallbackPermission.CompositeId(chatId, presserId, CallbackType.List, messageId);
        var hasPermission = await callbackRepository.HasPermission(id);
        if (!hasPermission) return await SendNotAccess(queryId, chatId);

        var maybeKeysAndCount = await listService.GetListBody(chatId, listType, currentOffset);
        return await maybeKeysAndCount.Match(
            tuple => EditSuccess(chatId, messageId, currentOffset, listType, tuple),
            () => SendNotFound(queryId, chatId));
    }

    private Task<Unit> EditSuccess(long chatId, int messageId, int currentOffset, ListType listType,
        (string, int) tuple)
    {
        var (keys, totalCount) = tuple;
        var keyboard = listButtons.GetListChatMarkup(totalCount, listType, currentOffset);
        var message = string.Format(appConfig.ListConfig.SuccessTemplate, listType, totalCount, keys);
        return messageAssistance.EditMessageAndLog(chatId, messageId, message, Prefix, replyMarkup: keyboard,
            ParseMode.MarkdownV2);
    }

    private Task<Unit> SendNotAccess(string queryId, long chatId)
    {
        var message = string.Format(appConfig.ListConfig.NotAccess, ListCommandHandler.CommandKey);
        return messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix, message);
    }

    private Task<Unit> SendNotFound(string queryId, long chatId)
    {
        var message = appConfig.ListConfig.NotFound;
        return messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix, message);
    }
}