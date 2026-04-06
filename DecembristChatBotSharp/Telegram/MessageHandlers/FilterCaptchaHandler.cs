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
    AppConfig appConfig,
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

        var maybeMessage = await filteredMessageRepository.GetFilteredMessage(new CompositeId(telegramId, chatId));
        return await maybeMessage.MatchAsync(async message =>
        {
            await messageAssistance.DeleteCommandMessage(chatId, message.CaptchaMessageId,
                nameof(FilterCaptchaHandler));
            return await HandleFailedCaptcha(chatId, telegramId, messageId, message, filterConfig);
        }, () => false);
    }

    private async Task<bool> HandleFailedCaptcha(
        long chatId, long telegramId, int suspiciousMessageId, FilteredMessage message, FilterConfig filterConfig)
    {
        var prevSuspiciousMessage = message.MessageId;
        var isFinalTry = message.TryCount >= appConfig.FilterJobConfig.CaptchaTryCount;
        if (isFinalTry)
        {
            await Task.WhenAll(banService.RestrictChatMember(chatId, telegramId),
                messageAssistance.SendFilterRestrictMessage(chatId, telegramId, suspiciousMessageId, filterConfig,
                    nameof(FilterCaptchaHandler)));
            await messageAssistance.DeleteCommandMessage(chatId, suspiciousMessageId, nameof(FilterCaptchaHandler));
        }

        await Array(
            messageAssistance.DeleteCommandMessage(chatId, prevSuspiciousMessage, nameof(FilterCaptchaHandler))
        ).WhenAll();
        return isFinalTry;
    }
}