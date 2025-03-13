using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.MessageHandlers;

public class FastReplyHandler(AppConfig appConfig, BotClient botClient)
{
    public async Task<Unit> Do(
        ChatMessageHandlerParams parameters,
        CancellationToken cancelToken)
    {
        var text = parameters.Text.ToLowerInvariant();
        if (!appConfig.FastReply.TryGetValue(text, out var reply)) return unit;
        
        var chatId = parameters.ChatId;
        var telegramId = parameters.TelegramId;
        var messageId = parameters.MessageId;
        return await Reply(chatId, telegramId, messageId, reply, text, cancelToken);
    }

    private async Task<Unit> Reply(
        long chatId,
        long telegramId,
        int messageId,
        string reply,
        string text,
        CancellationToken cancelToken)
    {
        var replyParameters = new ReplyParameters
        {
            MessageId = messageId
        };
        return await TryAsync(botClient.SendMessage(
            chatId,
            reply,
            replyParameters: replyParameters,
            cancellationToken: cancelToken)
        ).Match(
            _ => Log.Information("Sent fast reply to {0} text {1} in chat {2}", telegramId, text, chatId),
            ex => Log.Error(ex, "Failed to send fast reply to {0} text {1} in chat {2}", telegramId, text, chatId)
        );
    }
}