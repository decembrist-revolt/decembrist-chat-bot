using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class SuspiciousMessageHandler(
    WhiteListRepository whiteListRepository,
    SuspiciousMessageRepository suspiciousMessageRepository,
    BotClient botClient,
    CancellationTokenSource cancelToken,
    AppConfig appConfig)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        if (parameters.Payload is not TextPayload { Text: var text }) return false;
        var (messageId, telegramId, chatId) = parameters;

        if (await whiteListRepository.IsWhiteListMember((telegramId, chatId))) return false;
        if (!IsBlackWord(text)) return false;
        var text2 = string.Format(appConfig.BlackListConfig.CaptchaMessage, appConfig.BlackListConfig.CaptchaAnswer,
            appConfig.BlackListConfig.CaptchaTimeSeconds);

        return await botClient.SendMessage(chatId, text2,
                replyParameters: new ReplyParameters { MessageId = messageId }, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(async m =>
                {
                    var message = new SuspiciousMessage((chatId, messageId), telegramId, m.MessageId);
                    await suspiciousMessageRepository.AddSuspiciousMessage(message);

                    Log.Information("susp success");
                    return true;
                },
                ex =>
                {
                    Log.Error(ex, "susp ex");
                    return false;
                });
    }

    private bool IsBlackWord(string text)
    {
        return appConfig.BlackListConfig.Words != null &&
               appConfig.BlackListConfig.Words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }
}