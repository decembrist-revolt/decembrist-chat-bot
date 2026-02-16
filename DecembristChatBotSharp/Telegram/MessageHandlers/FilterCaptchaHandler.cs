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
                : await SendFailedCaptcha(chatId, m.Id.MessageId, filterConfig);
        }, () => false);
    }

    private async Task<bool> SendFailedCaptcha(long chatId, int suspiciousMessageId, FilterConfig filterConfig)
    {
        var text = filterConfig.FailedMessage;
        var buttons =
            await Array(
                messageAssistance.DeleteCommandMessage(chatId, suspiciousMessageId, nameof(FilterCaptchaHandler)),
                messageAssistance.SendMessage(chatId, text, nameof(FilterCaptchaHandler))).WhenAll();
        return false;
    }

    private async Task<bool> HandleSuccessCaptcha(long chatId, long telegramId, int messageId,
        FilterConfig filterConfig)
    {
        await whiteListRepository.AddWhiteListMember(new WhiteListMember(new CompositeId(telegramId, chatId)));
        await Array(
            messageAssistance.DeleteCommandMessage(chatId, messageId, nameof(FilterCaptchaHandler)),
            messageAssistance.SendMessageExpired(chatId, filterConfig.SuccessMessage, nameof(FilterCaptchaHandler))
        ).WhenAll();
        return true;
    }

    private bool IsCaptchaPassed(IMessagePayload payload, FilterConfig filterConfig) =>
        payload is TextPayload { Text: var text } &&
        string.Equals(filterConfig.CaptchaAnswer, text, StringComparison.CurrentCultureIgnoreCase);
}