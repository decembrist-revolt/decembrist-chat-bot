﻿using DecembristChatBotSharp.Mongo;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class FilterCaptchaHandler(
    FilteredMessageRepository filteredMessageRepository,
    MessageAssistance messageAssistance,
    AppConfig appConfig)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        var maybeMessage = filteredMessageRepository.GetFilteredMessage(chatId, telegramId);
        return await maybeMessage.MatchAsync(async m =>
        {
            await messageAssistance.DeleteCommandMessage(chatId, m.CaptchaMessageId, nameof(FilterCaptchaHandler));
            await filteredMessageRepository.DeleteFilteredMessage(m.Id);

            return IsCaptchaPassed(parameters.Payload)
                ? await SendSuccessCaptcha(chatId, messageId)
                : await SendFailedCaptcha(chatId, m.Id.MessageId);
        }, () => false);
    }

    private async Task<bool> SendFailedCaptcha(long chatId, int suspiciousMessageId)
    {
        var text = appConfig.FilterConfig.FailedMessage;
        await Array(
            messageAssistance.DeleteCommandMessage(chatId, suspiciousMessageId, nameof(FilterCaptchaHandler)),
            messageAssistance.SendCommandResponse(chatId, text, nameof(FilterCaptchaHandler))).WhenAll();
        return false;
    }

    private async Task<bool> SendSuccessCaptcha(long chatId, int messageId)
    {
        var text = appConfig.FilterConfig.SuccessMessage;
        await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, nameof(FilterCaptchaHandler)),
            messageAssistance.SendCommandResponse(chatId, text, nameof(FilterCaptchaHandler))).WhenAll();
        return true;
    }

    private bool IsCaptchaPassed(IMessagePayload payload) =>
        payload is TextPayload { Text: var text } &&
        string.Equals(appConfig.CaptchaConfig.CaptchaAnswer, text, StringComparison.CurrentCultureIgnoreCase);
}