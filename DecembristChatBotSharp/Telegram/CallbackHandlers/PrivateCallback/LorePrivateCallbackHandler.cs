using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Service.Buttons;
using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using DecembristChatBotSharp.Telegram.LoreHandlers;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;

[Singleton]
public class LorePrivateCallbackHandler(
    MessageAssistance messageAssistance,
    CallbackService callbackService,
    LoreButtons loreButtons,
    AppConfig appConfig,
    LoreService loreService) : IPrivateCallbackHandler
{
    public const string PrefixKey = "Lore";

    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, chatId, telegramId, messageId, queryId, maybeParameters) = queryParameters;

        if (!Enum.TryParse(suffix, true, out LoreSuffix loreSuffix)) return unit;

        var taskResult = maybeParameters.MatchAsync(
            None: () => messageAssistance.SendCommandResponse(chatId, "OK", nameof(ProfilePrivateCallbackHandler)),
            Some: async parameters =>
            {
                if (!callbackService.HasChatIdKey(parameters, out var targetChatId)) return unit;

                return loreSuffix switch
                {
                    LoreSuffix.Create => await SendRequestLoreKey(targetChatId, telegramId),
                    LoreSuffix.Delete => await SendRequestDelete(targetChatId, telegramId),
                    LoreSuffix.List when maybeParameters.IsSome => await SwitchLoreList(targetChatId, telegramId,
                        messageId, queryId, maybeParameters.ValueUnsafe()),
                    _ => throw new ArgumentOutOfRangeException(nameof(suffix), suffix, null)
                };
            });
        return await Array(taskResult, messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix)).WhenAll();
    }

    private Task<Unit> SendRequestDelete(long targetChatId, long chatId)
    {
        var message = string.Format(appConfig.LoreConfig.DeleteRequest,
            LoreService.GetLoreTag(LoreHandler.DeleteSuffix, targetChatId));
        return messageAssistance.SendCommandResponse(
            chatId, message, nameof(LorePrivateCallbackHandler), replyMarkup: loreService.GetKeyTip());
    }

    private Task<Unit> SendRequestLoreKey(long targetChatId, long chatId)
    {
        var message = string.Format(appConfig.LoreConfig.KeyRequest,
            LoreService.GetLoreTag(LoreHandler.KeySuffix, targetChatId));
        return messageAssistance.SendCommandResponse(
            chatId, message, nameof(LorePrivateCallbackHandler), replyMarkup: loreService.GetKeyTip());
    }

    private async Task<Unit> SwitchLoreList(long targetChatId, long telegramId, int messageId, string queryId,
        Map<string, string> parameters)
    {
        if (!loreService.IsContainIndex(parameters, out var currentOffset))
        {
            return await EditNotFound(targetChatId, telegramId, messageId);
        }

        var maybeKeysAndCount = await loreService.GetLoreKeys(targetChatId, currentOffset);
        return await maybeKeysAndCount.Match(
            None: () => messageAssistance.SendCommandResponse(targetChatId, "not keys", Prefix),
            Some: tuple =>
            {
                var (keys, totalCount) = tuple;
                var keyboard = loreButtons.GetLoreListPrivateMarkup(targetChatId, currentOffset, totalCount);
                var message = string.Format(appConfig.LoreListConfig.SuccessTemplate, totalCount, keys);
                return messageAssistance.EditProfileMessage(telegramId, targetChatId, messageId, keyboard, message,
                    Prefix, ParseMode.MarkdownV2);
            });
    }

    private Task<Unit> EditNotFound(long targetChatId, long telegramId, int messageId)
    {
        var markup = loreButtons.GetLoreMarkup(targetChatId);
        var message = appConfig.LoreConfig.PrivateLoreNotFound;
        return messageAssistance.EditProfileMessage(telegramId, targetChatId, messageId, markup, message, Prefix);
    }
}

public enum LoreSuffix
{
    Create,
    Delete,
    List,
}