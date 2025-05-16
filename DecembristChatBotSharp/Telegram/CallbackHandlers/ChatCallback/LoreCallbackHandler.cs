using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Service.Buttons;
using DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;

[Singleton]
public class LoreCallbackHandler(
    AppConfig appConfig,
    CallbackRepository callbackRepository,
    MessageAssistance messageAssistance,
    LoreButtons loreButtons,
    LoreService loreService) : IChatCallbackHandler
{
    public const string PrefixKey = "LoreChat";

    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, chatId, telegramId, messageId, queryId, maybeParameters) = queryParameters;
        if (!Enum.TryParse(suffix, true, out LoreChatSuffix loreSuffix)) return unit;

        return loreSuffix switch
        {
            LoreChatSuffix.List when maybeParameters.IsSome =>
                await SwitchLoreList(telegramId, chatId, messageId, queryId, maybeParameters.ValueUnsafe()),
            _ => throw new ArgumentOutOfRangeException(nameof(suffix), suffix, null)
        };
    }

    private async Task<Unit> SwitchLoreList(
        long presserId, long chatId, int messageId, string queryId, Map<string, string> parameters)
    {
        if (!loreService.IsContainIndex(parameters, out var currentOffset)) return unit;

        var id = new CallbackPermission.CompositeId(chatId, presserId, CallbackType.LoreList, messageId);
        var hasPermission = await callbackRepository.HasPermission(id);
        if (!hasPermission) return await SendNotAccess(queryId, chatId);

        var maybeKeysAndCount = await loreService.GetLoreKeys(chatId, currentOffset);
        return await maybeKeysAndCount.Match(
            tuple => EditSuccess(chatId, messageId, currentOffset, tuple),
            () => SendNotFound(queryId, chatId));
    }

    private Task<Unit> EditSuccess(long chatId, int messageId, int currentOffset, (string, int) tuple)
    {
        var (keys, totalCount) = tuple;
        var keyboard = loreButtons.GetLoreListChatMarkup(totalCount, currentOffset);
        var message = string.Format(appConfig.LoreListConfig.SuccessTemplate, totalCount, keys);
        return messageAssistance.EditMessageAndLog(chatId, messageId, message, Prefix, replyMarkup: keyboard,
            ParseMode.MarkdownV2);
    }

    private Task<Unit> SendNotAccess(string queryId, long chatId)
    {
        var message = string.Format(appConfig.LoreListConfig.NotAccess, LoreListCommandHandler.CommandKey);
        return messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix, message);
    }

    private Task<Unit> SendNotFound(string queryId, long chatId)
    {
        var message = appConfig.LoreListConfig.NotFound;
        return messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix, message);
    }
}

public enum LoreChatSuffix
{
    List
}