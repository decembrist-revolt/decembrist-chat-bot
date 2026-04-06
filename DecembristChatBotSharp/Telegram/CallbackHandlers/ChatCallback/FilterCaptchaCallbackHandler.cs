using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;

[Singleton]
public class FilterCaptchaCallbackHandler(
    FilteredMessageRepository filteredMessageRepository,
    WhiteListRepository whiteListRepository,
    MessageAssistance messageAssistance,
    ChatConfigService chatConfigService,
    CallbackRepository callbackRepository,
    CancellationTokenSource cancelToken,
    BanService banService) : IChatCallbackHandler
{
    public const string PrefixKey = "Captcha";

    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, chatId, telegramId, messageId, queryId, maybeParameters) = queryParameters;
        var id = new CallbackPermission.CompositeId(chatId, telegramId, CallbackType.Captcha, messageId);

        if (!await callbackRepository.HasPermission(id)) return await SendNotAccess(chatId, queryId);

        var maybeConfig = await chatConfigService.GetConfig(chatId, config => config.FilterConfig);
        if (!maybeConfig.TryGetSome(out var filterConfig))
        {
            return chatConfigService.LogNonExistConfig(unit, nameof(FilterConfig), Prefix);
        }

        var maybeMessage = await filteredMessageRepository.GetFilteredMessage(new CompositeId(telegramId, chatId));
        if (!maybeMessage.TryGetSome(out var message)) return unit;

        await messageAssistance.DeleteCommandMessage(chatId, message.CaptchaMessageId, PrefixKey);

        if (string.Equals(suffix, filterConfig.CaptchaAnswer, StringComparison.OrdinalIgnoreCase))
        {
            return await HandleCorrect(chatId, telegramId, message, filterConfig);
        }

        return await HandleWrongAnswer(chatId, telegramId, message, filterConfig);
    }

    private async Task<Unit> HandleCorrect(long chatId, long telegramId, FilteredMessage message,
        FilterConfig filterConfig)
    {
        Log.Information("User {0} passed filter captcha in chat {1}", telegramId, chatId);
        await filteredMessageRepository.DeleteFilteredMessage(message.Id);
        return await Array(
            whiteListRepository.AddWhiteListMember(new WhiteListMember(new CompositeId(telegramId, chatId))).ToUnit(),
            messageAssistance.SendMessageExpired(chatId, filterConfig.SuccessMessage, Prefix)
        ).WhenAll();
    }

    private async Task<Unit> HandleWrongAnswer(long chatId, long telegramId, FilteredMessage message,
        FilterConfig filterConfig)
    {
        Log.Information("User {0} failed filter captcha in chat {1}, user kicked", telegramId, chatId);
        var suspiciousMessageId = message.MessageId;
        await Task.WhenAll(banService.RestrictChatMember(chatId, telegramId),
            messageAssistance.SendFilterRestrictMessage(chatId, telegramId, suspiciousMessageId, filterConfig,
                Prefix));
        await messageAssistance.DeleteCommandMessage(chatId, suspiciousMessageId, Prefix);
        return unit;
    }

    private async Task<Unit> SendNotAccess(long chatId, string queryId)
    {
        var message = "Это сообщение для проходящего капчу";
        return await messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix, message);
    }
}