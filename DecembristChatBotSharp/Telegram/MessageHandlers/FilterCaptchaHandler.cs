using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class FilterCaptchaHandler(
    FilteredMessageRepository filteredMessageRepository,
    MessageAssistance messageAssistance,
    ChatConfigService chatConfigService)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        var maybeConfig = await chatConfigService.GetConfig(chatId, config => config.FilterConfig);
        if (!maybeConfig.TryGetSome(out var filterConfig))
            return chatConfigService.LogNonExistConfig(false, nameof(FilterConfig));

        var maybeCaptchaConfig = await chatConfigService.GetConfig(chatId, config => config.CaptchaConfig);
        if (!maybeCaptchaConfig.TryGetSome(out var captchaConfig))
            return chatConfigService.LogNonExistConfig(false, nameof(Entity.Configs.CaptchaConfig));


        var maybeMessage = filteredMessageRepository.GetFilteredMessage(chatId, telegramId);
        return await maybeMessage.MatchAsync(async m =>
        {
            await messageAssistance.DeleteCommandMessage(chatId, m.CaptchaMessageId, nameof(FilterCaptchaHandler));
            await filteredMessageRepository.DeleteFilteredMessage(m.Id);

            return IsCaptchaPassed(parameters.Payload, captchaConfig)
                ? await SendSuccessCaptcha(chatId, messageId, filterConfig)
                : await SendFailedCaptcha(chatId, m.Id.MessageId, filterConfig);
        }, () => false);
    }

    private async Task<bool> SendFailedCaptcha(long chatId, int suspiciousMessageId, FilterConfig filterConfig)
    {
        var text = filterConfig.FailedMessage;
        await Array(
            messageAssistance.DeleteCommandMessage(chatId, suspiciousMessageId, nameof(FilterCaptchaHandler)),
            messageAssistance.SendCommandResponse(chatId, text, nameof(FilterCaptchaHandler))).WhenAll();
        return false;
    }

    private async Task<bool> SendSuccessCaptcha(long chatId, int messageId, FilterConfig filterConfig)
    {
        await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, nameof(FilterCaptchaHandler)),
            messageAssistance.SendCommandResponse(chatId, filterConfig.SuccessMessage, nameof(FilterCaptchaHandler))).WhenAll();
        return true;
    }

    private bool IsCaptchaPassed(IMessagePayload payload, Entity.Configs.CaptchaConfig captchaConfig) =>
        payload is TextPayload { Text: var text } &&
        string.Equals(captchaConfig.CaptchaAnswer, text, StringComparison.CurrentCultureIgnoreCase);
}