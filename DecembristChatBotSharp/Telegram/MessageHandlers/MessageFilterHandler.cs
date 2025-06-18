using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class MessageFilterHandler(
    WhiteListRepository whiteListRepository,
    MessageFilterRepository messageFilterRepository,
    BotClient botClient,
    CancellationTokenSource cancelToken,
    AppConfig appConfig)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        if (parameters.Payload is not TextPayload { Text: var text }) return false;
        var (messageId, telegramId, chatId) = parameters;

        if (!IsBlackWord(text) || await whiteListRepository.IsWhiteListMember((telegramId, chatId))) return false;
        var messageText = string.Format(appConfig.MessageFilterConfig.CaptchaMessage,
            appConfig.MessageFilterConfig.CaptchaAnswer,
            appConfig.MessageFilterConfig.CaptchaTimeSeconds);

        return await botClient.SendMessage(chatId, messageText,
                replyParameters: new ReplyParameters { MessageId = messageId },
                cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(async m =>
                {
                    var message = new FilteredMessage((chatId, messageId), telegramId, m.MessageId, DateTime.UtcNow);
                    await messageFilterRepository.AddFilteredMessage(message);

                    Log.Information("Success create suspicious message {0}, author: {1}", message.Id, telegramId);
                    return true;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to create suspicious message in chat {0}, author: {1}", chatId, telegramId);
                    return false;
                });
    }

    private bool IsBlackWord(string text) =>
        appConfig.MessageFilterConfig.FilterWords != null &&
        appConfig.MessageFilterConfig.FilterWords.Any(w =>
            text.Contains(w, StringComparison.OrdinalIgnoreCase));
}