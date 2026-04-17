using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Service.Buttons;
using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class FilteredMessageHandler(
    FilterCaptchaButtons filterCaptchaButtons,
    CallbackRepository callbackRepository,
    FilteredMessageRepository filteredMessageRepository,
    BotClient botClient,
    AppConfig appConfig,
    FilterCaptchaService filterCaptchaService,
    CancellationTokenSource cancelToken,
    ChatConfigService chatConfigService,
    MessageAssistance messageAssistance,
    BanService banService)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        var maybeFilterConfig = await chatConfigService.GetConfig(chatId, config => config.FilterConfig);
        if (!maybeFilterConfig.TryGetSome(out var filterConfig))
            return chatConfigService.LogNonExistConfig(false, nameof(FilterConfig), nameof(FilteredMessageHandler));

        var maybeExisting = await filteredMessageRepository.GetFilteredMessage((telegramId, chatId));
        var isExisting = maybeExisting.TryGetSome(out var existingMessage);
        if (isExisting)
        {
            var isBanned = await HandleFailedCaptcha(chatId, telegramId, messageId, existingMessage, filterConfig);
            if (isBanned) return true;
        }

        if (!isExisting && !await filterCaptchaService.IsSuspectMessage(parameters)) return false;
        return await SendCaptchaMessage(chatId, messageId, telegramId, filterConfig, maybeExisting);
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
                    nameof(FilteredMessageHandler)));
            await messageAssistance.DeleteCommandMessage(chatId, suspiciousMessageId, nameof(FilteredMessageHandler));
        }

        await Array(
            messageAssistance.DeleteCommandMessage(chatId, message.CaptchaMessageId, nameof(FilteredMessageHandler)),
            messageAssistance.DeleteCommandMessage(chatId, prevSuspiciousMessage, nameof(FilteredMessageHandler))
        ).WhenAll();
        return isFinalTry;
    }

    private async Task<bool> SendCaptchaMessage(long chatId, int messageId, long telegramId, FilterConfig filterConfig,
        Option<FilteredMessage> maybeMessage)
    {
        var tryCount = maybeMessage.Match(message => message.TryCount, () => 0);
        var messageText = string.Format(
            filterConfig.CaptchaMessage, filterConfig.CaptchaAnswer, appConfig.FilterJobConfig.CaptchaTimeSeconds,
            appConfig.FilterJobConfig.CaptchaTryCount - tryCount);
        var replyMarkup = filterCaptchaButtons.GetMarkup(telegramId, filterConfig.CaptchaAnswer,
            FilterCaptchaCallbackHandler.PrefixKey);

        return await botClient.SendMessage(chatId, messageText, replyMarkup: replyMarkup,
                replyParameters: new ReplyParameters { MessageId = messageId }, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(async m =>
                {
                    var expireAt = DateTime.UtcNow.AddSeconds(appConfig.FilterJobConfig.CaptchaTimeSeconds);
                    var permission = new CallbackPermission(
                        new CallbackPermission.CompositeId(chatId, telegramId, CallbackType.Filter, m.MessageId),
                        expireAt);
                    await callbackRepository.AddCallbackPermission(permission);

                    var filteredMessage =
                        new FilteredMessage((telegramId, chatId), messageId, m.MessageId, DateTime.UtcNow);
                    await filteredMessageRepository.AddFilteredMessage(filteredMessage);

                    Log.Information("Success create filtered message {0}, author: {1}", filteredMessage.Id, telegramId);
                    return true;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to create filtered message in chat {0}, author: {1}", chatId, telegramId);
                    return false;
                });
    }
}