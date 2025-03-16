using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.MessageHandlers;

public class PrivateMessageHandler(AppConfig appConfig, BotClient botClient)
{
    private const string MeCommand = "/me";
    private const string StatusCommand = "/status";

    public async Task<Unit> Do(Message message, CancellationToken cancelToken)
    {
        var chatId = message.Chat.Id;
        var type = message.Type;
        var telegramId = message.From!.Id;
        var trySend = type switch
        {
            MessageType.Sticker => SendStickerFileId(chatId, message.Sticker!.FileId, cancelToken),
            MessageType.Text when message.Text == MeCommand => SendMe(telegramId, chatId, cancelToken),
            MessageType.Text when message.Text == StatusCommand => SendStatus(chatId, cancelToken),
            MessageType.Text when message.Text is {} text && text.StartsWith(FastReplyHandler.StickerPrefix) => 
                SendSticker(chatId, text[FastReplyHandler.StickerPrefix.Length..], cancelToken),
            _ => TryAsync(botClient.SendMessage(chatId, "OK", cancellationToken: cancelToken))
        };
        return await trySend.Match(
            message => Log.Information("Sent private {0} to {1}", message.Text?.Replace('\n', ' '), telegramId),
            ex => Log.Error(ex, "Failed to send private message to {0}", telegramId)
        );
    }

    private TryAsync<Message> SendStickerFileId(long chatId, string fileId, CancellationToken cancelToken)
    {
        var message = $"*Sticker fileId*\n\n`{FastReplyHandler.StickerPrefix}{fileId}`";
        return TryAsync(botClient.SendMessage(
            chatId,
            message,
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: cancelToken)
        );
    }

    private TryAsync<Message> SendMe(long telegramId, long chatId, CancellationToken cancelToken)
    {
        var message = $"*Your id*\n\n`{telegramId}`";
        return TryAsync(botClient.SendMessage(
            chatId,
            message,
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: cancelToken)
        );
    }

    private TryAsync<Message> SendStatus(long chatId, CancellationToken cancelToken)
    {
        var message = $"*Deploy time utc*\n\n`{appConfig.DeployTime}`";
        return TryAsync(botClient.SendMessage(
            chatId,
            message,
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: cancelToken)
        );
    }
    
    private TryAsync<Message> SendSticker(long chatId, string fileId, CancellationToken cancelToken) =>
        TryAsync(botClient.SendSticker(
            chatId,
            new InputFileId(fileId),
            cancellationToken: cancelToken)
        );
}