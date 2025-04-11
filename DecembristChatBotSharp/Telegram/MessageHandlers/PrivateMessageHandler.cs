using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class PrivateMessageHandler(
    AppConfig appConfig,
    BotClient botClient,
    MessageAssistance messageAssistance,
    InventoryService inventoryService,
    CancellationTokenSource cancelToken)
{
    public const string StartCommand = "/start";
    public const string InventoryCommandSuffix = "getInventoryForChat=";

    private const string MeCommand = "/me";
    private const string StatusCommand = "/status";
    private const string InventoryCommand = StartCommand + " " + InventoryCommandSuffix;

    public async Task<Unit> Do(Message message)
    {
        var chatId = message.Chat.Id;
        var type = message.Type;
        var telegramId = message.From!.Id;
        var trySend = type switch
        {
            MessageType.Sticker => SendStickerFileId(chatId, message.Sticker!.FileId),
            MessageType.Text when message.Text == MeCommand => SendMe(telegramId, chatId),
            MessageType.Text when message.Text?.Contains(InventoryCommand) == true
                                  && message.Text.Split("=") is [_, var chatIdText]
                                  && long.TryParse(chatIdText, out var targetChatId) =>
                await SendInventory(telegramId, targetChatId),
            MessageType.Text when message.Text == StatusCommand => SendStatus(chatId),
            MessageType.Text when message.Text is { } text && text.StartsWith(FastReplyHandler.StickerPrefix) =>
                SendSticker(chatId, text[FastReplyHandler.StickerPrefix.Length..]),
            _ => TryAsync(botClient.SendMessage(chatId, "OK", cancellationToken: cancelToken.Token))
        };
        return await trySend.Match(
            message => Log.Information("Sent private {0} to {1}", message.Text?.Replace('\n', ' '), telegramId),
            ex => Log.Error(ex, "Failed to send private message to {0}", telegramId)
        );
    }

    private TryAsync<Message> SendStickerFileId(long chatId, string fileId)
    {
        var message = $"*Sticker fileId*\n\n`{FastReplyHandler.StickerPrefix}{fileId}`";
        return TryAsync(botClient.SendMessage(
            chatId,
            message,
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: cancelToken.Token)
        );
    }

    private async Task<TryAsync<Message>> SendInventory(long telegramId, long chatId)
    {
        var message = await inventoryService.GetInventory(chatId, telegramId);
        return TryAsync(botClient.SendMessage(telegramId, message,
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: cancelToken.Token));
    }


    private TryAsync<Message> SendMe(long telegramId, long chatId)
    {
        var message = $"*Your id*\n\n`{telegramId}`";
        return TryAsync(botClient.SendMessage(
            chatId,
            message,
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: cancelToken.Token)
        );
    }

    private TryAsync<Message> SendStatus(long chatId)
    {
        var message = $"*Deploy time utc*\n\n`{appConfig.DeployTime}`";
        return TryAsync(botClient.SendMessage(
            chatId,
            message,
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: cancelToken.Token)
        );
    }

    private TryAsync<Message> SendSticker(long chatId, string fileId) =>
        TryAsync(botClient.SendSticker(
            chatId,
            fileId,
            cancellationToken: cancelToken.Token)
        ).BiMap(TryAsync, async ex =>
        {
            await messageAssistance.SendStickerNotFound(chatId, fileId);
            return TryAsync<Message>(ex);
        }).Flatten();
}