using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Service.Buttons;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class FilteredMessageHandler(
    CaptchaButtons captchaButtons,
    CallbackRepository callbackRepository,
    FilteredMessageRepository filteredMessageRepository,
    BotClient botClient,
    AppConfig appConfig,
    FilterCaptchaService filterCaptchaService,
    CancellationTokenSource cancelToken,
    ChatConfigService chatConfigService)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (!await filterCaptchaService.IsSuspectMessage(parameters)) return false;
        return await SendCaptchaMessage(chatId, messageId, telegramId);
    }

    private async Task<bool> SendCaptchaMessage(long chatId, int messageId, long telegramId)
    {
        var maybeFilterConfig = await chatConfigService.GetConfig(chatId, config => config.FilterConfig);
        if (!maybeFilterConfig.TryGetSome(out var filterConfig))
        {
            return chatConfigService.LogNonExistConfig(false, nameof(FilterConfig),
                nameof(FilteredMessageHandler));
        }

        var maybeMessage = await filteredMessageRepository.GetFilteredMessage((telegramId, chatId));
        var tryCount = maybeMessage.Match(message => message.TryCount, () => 0);
        var messageText = string.Format(
            filterConfig.CaptchaMessage, filterConfig.CaptchaAnswer, appConfig.FilterJobConfig.CaptchaTimeSeconds,
            appConfig.FilterJobConfig.CaptchaTryCount - tryCount);
        var replyMarkup = captchaButtons.GetMarkup(telegramId, filterConfig.CaptchaAnswer);

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
                    
                    var message = new FilteredMessage((telegramId, chatId), messageId, m.MessageId, DateTime.UtcNow);
                    await filteredMessageRepository.AddFilteredMessage(message);

                    Log.Information("Success create filtered message {0}, author: {1}", message.Id, telegramId);
                    return true;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to create filtered message in chat {0}, author: {1}", chatId, telegramId);
                    return false;
                });
    }
}