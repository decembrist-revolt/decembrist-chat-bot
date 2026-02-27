using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class FilterCaptchaHandler(
    FilteredMessageRepository filteredMessageRepository,
    MessageAssistance messageAssistance,
    ChatConfigService chatConfigService,
    BanService banService,
    WhiteListRepository whiteListRepository)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        var maybeConfig = await chatConfigService.GetConfig(chatId, config => config.FilterConfig);
        if (!maybeConfig.TryGetSome(out var filterConfig))
        {
            return chatConfigService.LogNonExistConfig(false, nameof(FilterConfig));
        }

        var maybeMessage = filteredMessageRepository.GetFilteredMessage(chatId, telegramId);
        return await maybeMessage.MatchAsync(async m =>
        {
            await messageAssistance.DeleteCommandMessage(chatId, m.CaptchaMessageId, nameof(FilterCaptchaHandler));
            await filteredMessageRepository.DeleteFilteredMessage(m.Id);

            return IsCaptchaPassed(parameters.Payload, filterConfig)
                ? await HandleSuccessCaptcha(chatId, telegramId, messageId, filterConfig)
                : await HandleFailedCaptcha(chatId, telegramId, messageId, m.Id.MessageId, filterConfig);
        }, () => false);
    }

    private async Task<bool> HandleFailedCaptcha(
        long chatId, long telegramId, int captchaMessageId, int suspiciousMessageId, FilterConfig filterConfig)
    {
        await messageAssistance.SendFilterRestrictMessage(chatId, telegramId, suspiciousMessageId, filterConfig,
            nameof(FilterCaptchaHandler));
        await Array(
            banService.RestrictChatMember(chatId, telegramId),
            messageAssistance.DeleteCommandMessage(chatId, suspiciousMessageId, nameof(FilterCaptchaHandler)),
            messageAssistance.DeleteCommandMessage(chatId, captchaMessageId, nameof(FilterCaptchaHandler))
        ).WhenAll();
        return true;
    }

    private async Task<bool> HandleSuccessCaptcha(long chatId, long telegramId, int messageId,
        FilterConfig filterConfig)
    {
        await Array(
            whiteListRepository.AddWhiteListMember(new WhiteListMember(new CompositeId(telegramId, chatId))).ToUnit(),
            messageAssistance.DeleteCommandMessage(chatId, messageId, nameof(FilterCaptchaHandler)),
            messageAssistance.SendMessageExpired(chatId, filterConfig.SuccessMessage, nameof(FilterCaptchaHandler))
        ).WhenAll();
        return false;
    }

    private bool IsCaptchaPassed(IMessagePayload payload, FilterConfig filterConfig) =>
        payload is TextPayload { Text: var text } &&
        string.Equals(filterConfig.CaptchaAnswer, text, StringComparison.CurrentCultureIgnoreCase);
}