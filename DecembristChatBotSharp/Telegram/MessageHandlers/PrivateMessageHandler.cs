using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram.LoreHandlers;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class PrivateMessageHandler(
    AppConfig appConfig,
    BotClient botClient,
    MessageAssistance messageAssistance,
    InventoryService inventoryService,
    ProfileService profileService,
    LoreHandler loreHandler,
    CancellationTokenSource cancelToken)
{
    public const string StartCommand = "/start";
    public const string SplitSymbol = "=";
    public const string ProfileCommand = "/profile";
    public const string InventoryCommandSuffix = "getInventoryForChat";

    private const string MeCommand = "/me";
    private const string StatusCommand = "/status";
    private const string InventoryCommand = StartCommand + " " + InventoryCommandSuffix + SplitSymbol;
    public static string GetCommandForChat(string command, long chatId) => command + SplitSymbol + chatId;

    public async Task<Unit> Do(Message message)
    {
        var chatId = message.Chat.Id;
        var type = message.Type;
        var telegramId = message.From!.Id;

        var trySend = type switch
        {
            MessageType.Sticker => SendStickerFileId(chatId, message.Sticker!.FileId),
            MessageType.Text when message.Text == MeCommand => SendMe(telegramId, chatId),
            MessageType.Text when message.Text == ProfileCommand => SendMenuButton(chatId),
            MessageType.Text when message.Text?.Contains(InventoryCommand) == true
                                  && message.Text.Split("=") is [_, var chatIdText]
                                  && long.TryParse(chatIdText, out var targetChatId) =>
                await SendInventory(chatId, targetChatId),
            MessageType.Text when message.Text == StatusCommand => SendStatus(chatId),
            MessageType.Text when message.Text is { } text && text.StartsWith(FastReplyHandler.StickerPrefix) =>
                SendSticker(chatId, text[FastReplyHandler.StickerPrefix.Length..]),
            MessageType.Text when message is { Text: not null, ReplyToMessage.Text: { } replyText }
                                  && replyText.Contains(LoreHandler.Tag) => loreHandler.Do(message).ToTryAsync(),
            MessageType.ChatShared when message.ChatShared is { ChatId: var sharedChatId } =>
                await SendProfile(sharedChatId, chatId),
            _ => TryAsync(botClient.SendMessage(chatId, "OK", cancellationToken: cancelToken.Token))
        };
        return await trySend.Match(
            message => Log.Information("Sent private {0} to {1}", message.Text?.Replace('\n', ' '), telegramId),
            ex => Log.Error(ex, "Failed to send private message to {0}", telegramId)
        );
    }

    private async Task<TryAsync<Message>> SendProfile(long chatId, long privateChatId)
    {
        if (!IsAllowedChat(chatId)) return SendChatNotAllowed(privateChatId);
        var message = appConfig.MenuConfig.WelcomeMessage;
        var markup = await profileService.GetProfileMarkup(privateChatId, chatId);
        return TryAsync(botClient.SendMessage(privateChatId, message,
            replyMarkup: markup,
            cancellationToken: cancelToken.Token));
    }

    private bool IsAllowedChat(long chatId) => appConfig.AllowedChatConfig.AllowedChatIds?.Contains(chatId) == true;

    private TryAsync<Message> SendChatNotAllowed(long chatId)
    {
        var message = appConfig.MenuConfig.ChatNotAllowed;
        return botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token).ToTryAsync();
    }

    private TryAsync<Message> SendMenuButton(long chatId)
    {
        var message = appConfig.MenuConfig.WelcomeMessage;
        var buttons = new ReplyKeyboardMarkup(new[]
        {
            KeyboardButton.WithRequestChat("Check profile", 1, false),
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };
        return TryAsync(botClient.SendMessage(chatId, message, replyMarkup: buttons,
            cancellationToken: cancelToken.Token));
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

    private async Task<TryAsync<Message>> SendInventory(long privateChatId, long chatId)
    {
        var message = await inventoryService.GetInventory(chatId, privateChatId);
        return TryAsync(botClient.SendMessage(privateChatId, message,
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