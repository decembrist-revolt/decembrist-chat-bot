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
    MemberItemRepository memberItemRepository,
    BotClient botClient,
    CancellationTokenSource cancelToken)
{
    public const string DollarPrefix = "$";
    public const string StickerPrefix = $"{DollarPrefix}sticker_";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (await memberItemRepository.IsUserHasItem(chatId, telegramId, MemberItemType.Stone)) return unit;

        var maybeReply = parameters.Payload switch
        {
            TextPayload { Text: var text } => await db.FindOne(chatId, text, FastReplyType.Text),
            StickerPayload { FileId: var fileId } => await db.FindOne(chatId, fileId, FastReplyType.Sticker),
            _ => None
        };

        var trySend =
            from reply in maybeReply.ToTryOptionAsync()
            from message in SendReply(reply, chatId, messageId).ToTryOption()
            select fun(() =>
                Log.Information("Sent fast reply to {0} payload {1} in chat {2}", telegramId, reply.Reply, chatId));

        return await trySend.IfFail(ex =>
            Log.Error(ex, "Failed to send fast reply to {0} payload {1} in chat {2}", telegramId, maybeReply, chatId));
    }

    private Task<Message> SendReply(FastReply reply, long chatId, int messageId) =>
        reply.ReplyType switch
        {
            FastReplyType.Sticker => botClient.SendSticker(
                chatId,
                new InputFileId(reply.Reply),
                replyParameters: new ReplyParameters { MessageId = messageId },
                cancellationToken: cancelToken.Token),

            FastReplyType.Text => botClient.SendMessage(
                chatId,
                reply.Reply,
                replyParameters: new ReplyParameters { MessageId = messageId },
                cancellationToken: cancelToken.Token),

            _ => throw new ArgumentException($"Unsupported reply type: {reply.ReplyType}")
        };
}