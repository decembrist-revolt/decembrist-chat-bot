using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.MessageHandlers;

public class FastReplyHandler(AppConfig appConfig, BotClient botClient)
{
    private const string StickerPrefix = "$sticker:";
    
    public async Task<Unit> Do(
        ChatMessageHandlerParams parameters,
        CancellationToken cancelToken)
    {
        var text = parameters.Text.ToLowerInvariant();
        var sticker = parameters.Sticker;
        var reply = "";
        if (text != "" && !appConfig.FastReply.TextMessage.TryGetValue(text, out reply)) return unit;
        else if (sticker != "" && !appConfig.FastReply.StickerMessage.TryGetValue(sticker, out reply)) return unit;
        var chatId = parameters.ChatId;
        var telegramId = parameters.TelegramId;
        var messageId = parameters.MessageId;
        return await Reply(chatId, telegramId, messageId, reply, text, sticker, cancelToken);
    }

    private async Task<Unit> Reply(
        long chatId,
        long telegramId,
        int messageId,
        string reply,
        string text,
        string sticker,
        CancellationToken cancelToken)
    {
        var replyParameters = new ReplyParameters
        {
            MessageId = messageId
        };

        var tryAsync = GetReplyType(reply) switch
        {
            ReplyType.Text => SendMessage(chatId, reply, replyParameters, cancelToken),
            ReplyType.Sticker => SendSticker(chatId, reply, replyParameters, cancelToken),
            _ => TryAsyncFail<Message>(new Exception("Unknown reply type"))
        };
        return await tryAsync.Match(
            _ => Log.Information("Sent fast reply to {0} text {1} in chat {2}", telegramId, text, chatId),
            ex => Log.Error(ex, "Failed to send fast reply to {0} text {1} in chat {2}", telegramId, text, chatId)
        );
    }
    
    private ReplyType GetReplyType(string reply) => reply.StartsWith(StickerPrefix) ? ReplyType.Sticker : ReplyType.Text;

    private TryAsync<Message> SendMessage(long chatId, string reply, ReplyParameters replyParameters, CancellationToken cancelToken)
    {
        return TryAsync(botClient.SendMessage(
            chatId,
            reply,
            replyParameters: replyParameters,
            cancellationToken: cancelToken)
        );
    }
    
    private TryAsync<Message> SendSticker(long chatId, string reply, ReplyParameters replyParameters, CancellationToken cancelToken)
    {
        var fileId = reply[StickerPrefix.Length..];
        return TryAsync(botClient.SendSticker(
            chatId,
            new InputFileId(fileId),
            replyParameters: replyParameters,
            cancellationToken: cancelToken)
        );
    }

    private enum ReplyType
    {
        Text,
        Sticker
    }
}
