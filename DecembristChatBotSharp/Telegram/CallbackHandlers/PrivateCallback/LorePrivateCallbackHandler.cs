using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Service.Buttons;
using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using DecembristChatBotSharp.Telegram.LoreHandlers;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;

[Singleton]
public class LorePrivateCallbackHandler(
    MessageAssistance messageAssistance,
    CallbackService callbackService,
    LoreButtons loreButtons,
    ChatConfigService chatConfigService,
    LoreService loreService) : IPrivateCallbackHandler
{
    public const string PrefixKey = "Lore";

    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, chatId, telegramId, messageId, queryId, maybeParameters) = queryParameters;

        if (!Enum.TryParse(suffix, true, out LoreSuffix loreSuffix)) return unit;

        var taskResult = maybeParameters.MatchAsync(
            None: () => messageAssistance.SendCommandResponse(chatId, "OK", nameof(LorePrivateCallbackHandler)),
            Some: async parameters =>
            {
                if (!callbackService.HasChatIdKey(parameters, out var targetChatId) &&
                    !await messageAssistance.IsAllowedChat(targetChatId)) return unit;
                var maybeLoreConfig = await chatConfigService.GetConfig(targetChatId, config => config.LoreConfig);
                if (!maybeLoreConfig.TryGetSome(out var loreConfig))
                {
                    return chatConfigService.LogNonExistConfig(unit, nameof(LoreConfig), Prefix);
                }

                return loreSuffix switch
                {
                    LoreSuffix.Create => await SendRequestLoreKey(targetChatId, telegramId, loreConfig),
                    LoreSuffix.Delete => await SendRequestDelete(targetChatId, telegramId, loreConfig),
                    LoreSuffix.List when maybeParameters.IsSome => await SwitchLoreList(targetChatId, telegramId,
                        messageId, queryId, maybeParameters.ValueUnsafe(), loreConfig),
                    _ => throw new ArgumentOutOfRangeException(nameof(suffix), suffix, null)
                };
            });
        return await Array(taskResult, messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix)).WhenAll();
    }

    private async Task<Unit> SendRequestDelete(long targetChatId, long chatId, LoreConfig loreConfig)
    {
        var message = string.Format(loreConfig.DeleteRequest,
            LoreService.GetLoreTag(LoreHandler.DeleteSuffix, targetChatId));
        return await messageAssistance.SendCommandResponse(
            chatId, message, nameof(LorePrivateCallbackHandler), replyMarkup: loreService.GetKeyTip());
    }

    private async Task<Unit> SendRequestLoreKey(long targetChatId, long chatId, LoreConfig loreConfig)
    {
        var message = string.Format(loreConfig.KeyRequest, LoreService.GetLoreTag(LoreHandler.KeySuffix, targetChatId));
        return await messageAssistance.SendCommandResponse(
            chatId, message, nameof(LorePrivateCallbackHandler), replyMarkup: loreService.GetKeyTip());
    }

    private async Task<Unit> SwitchLoreList(long targetChatId, long telegramId, int messageId, string queryId,
        Map<string, string> parameters, LoreConfig loreConfig)
    {
        if (!loreService.IsContainIndex(parameters, out var currentOffset))
        {
            return await EditNotFound(targetChatId, telegramId, messageId, loreConfig);
        }

        var maybeListConfig = await chatConfigService.GetConfig(targetChatId, config => config.ListConfig);
        if (!maybeListConfig.TryGetSome(out var listConfig))
            return chatConfigService.LogNonExistConfig(unit, nameof(ListConfig));

        var maybeKeysAndCount = await loreService.GetLoreKeys(targetChatId, currentOffset);
        return await maybeKeysAndCount.Match(
            None: () => messageAssistance.SendCommandResponse(targetChatId, "not keys", Prefix),
            Some: tuple =>
            {
                var (keys, totalCount) = tuple;
                var keyboard = loreButtons.GetLoreListPrivateMarkup(targetChatId, currentOffset, totalCount);
                var message = string.Format(listConfig.SuccessTemplate, ListType.Lore, totalCount, keys);
                return messageAssistance.EditProfileMessage(telegramId, targetChatId, messageId, keyboard, message,
                    Prefix, ParseMode.MarkdownV2);
            });
    }

    private async Task<Unit> EditNotFound(long targetChatId, long telegramId, int messageId, LoreConfig loreConfig)
    {
        var markup = loreButtons.GetLoreMarkup(targetChatId);
        var message = loreConfig.PrivateLoreNotFound;
        return await messageAssistance.EditProfileMessage(telegramId, targetChatId, messageId, markup, message, Prefix);
    }
}

public enum LoreSuffix
{
    Create,
    Delete,
    List,
}