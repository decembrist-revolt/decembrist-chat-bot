using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class FastReplyHandler(
    FastReplyRepository db,
    BotClient botClient,
    CancellationTokenSource cancelToken)
{
    public const string DollarPrefix = "$";
    public const string StickerPrefix = $"{DollarPrefix}sticker_";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var chatId = parameters.ChatId;
        var telegramId = parameters.TelegramId;
        var messageId = parameters.MessageId;

        var maybeReply = parameters.Payload switch
        {
            TextPayload { Text: var text } => await db.FindOne(chatId, text, FastReplyType.Text),
            StickerPayload { FileId: var fileId } => await db.FindOne(chatId, fileId, FastReplyType.Sticker),
            _ => None
        };

        return await maybeReply.Match(
            reply => HandleReply(chatId, telegramId, messageId, reply.Reply, reply.ReplyType),
            () => Task.FromResult(unit));
    }

    private Task<Unit> HandleReply(
        long chatId,
        long telegramId,
        int messageId,
        string reply,
        FastReplyType replyType) => TrySendReply(chatId, messageId, reply, replyType)
        .Match(
            _ => Log.Information("Sent fast reply to {0} payload {1} in chat {2}", telegramId, reply, chatId),
            ex => Log.Error(ex, "Failed to send fast reply to {0} payload {1} in chat {2}", telegramId, reply, chatId)
        );

    private TryAsync<Message> TrySendReply(
        long chatId,
        int messageId,
        string reply,
        FastReplyType type) => TryAsync(type switch
    {
        FastReplyType.Sticker => botClient.SendSticker(
            chatId,
            new InputFileId(reply),
            replyParameters: new ReplyParameters { MessageId = messageId },
            cancellationToken: cancelToken.Token),
        FastReplyType.Text => botClient.SendMessage(
            chatId,
            reply,
            replyParameters: new ReplyParameters { MessageId = messageId },
            cancellationToken: cancelToken.Token),
        _ => throw new ArgumentNullException(nameof(type))
    });
}