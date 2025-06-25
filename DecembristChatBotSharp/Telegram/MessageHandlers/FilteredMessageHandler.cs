using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class FilteredMessageHandler(
    WhiteListRepository whiteListRepository,
    FilterRecordRepository filterRecordRepository,
    FilteredMessageRepository filteredMessageRepository,
    BotClient botClient,
    CancellationTokenSource cancelToken,
    AppConfig appConfig)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        if (parameters.Payload is not TextPayload { Text: var text }) return false;
        var (messageId, telegramId, chatId) = parameters;

        if (!await IsFiltered(text, chatId) || await whiteListRepository.IsWhiteListMember((telegramId, chatId)))
            return false;
        var messageText = string.Format(appConfig.FilterConfig.CaptchaMessage,
            appConfig.FilterConfig.CaptchaAnswer,
            appConfig.FilterConfig.CaptchaTimeSeconds);

        return await botClient.SendMessage(chatId, messageText,
                replyParameters: new ReplyParameters { MessageId = messageId },
                cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(async m =>
                {
                    var message = new FilteredMessage((chatId, messageId), telegramId, m.MessageId, DateTime.UtcNow);
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

    private async Task<bool> IsFiltered(string text, long chatId) =>
        (appConfig.FilterConfig.FilterPhrases != null && appConfig.FilterConfig.FilterPhrases.Any(w =>
            text.Contains(w, StringComparison.OrdinalIgnoreCase))) ||
        await filterRecordRepository.IsFilterRecordExist((chatId, text));
}