using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Service.Buttons;
using DecembristChatBotSharp.Telegram.LoreHandlers;
using DecembristChatBotSharp.Telegram.MessageHandlers.PrivateMessage;
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
    ProfileButtons profileButtons,
    GlobalAdminButton globalAdminButton,
    LoreHandler loreHandler,
    FilterRecordHandler filterRecordHandler,
    ChatConfigHandler chatConfigHandler,
    MazeGameJoinCommandHandler mazeGameJoinCommandHandler,
    ChatConfigService chatConfigService,
    CancellationTokenSource cancelToken)
{
    public const string StartCommand = "/start";
    public const string SplitSymbol = "=";
    public const string ProfileCommand = "/profile";
    public const string InventoryCommandSuffix = "getInventoryForChat";
    public const string MazeGameCommandSuffix = "mazeGameInvite";

    private const string MeCommand = "/me";
    private const string StatusCommand = "/status";
    private const string GlobalAdminCommand = "/admin";
    private const string InventoryCommand = StartCommand + " " + InventoryCommandSuffix + SplitSymbol;
    private const string MazeGameInviteCommand = StartCommand + " " + MazeGameCommandSuffix + SplitSymbol;
    private const string GreetingsButtonText = "Добро пожаловать, нажмите на кнопку, чтобы продолжить";
    public static string GetCommandForChat(string command, long chatId) => command + SplitSymbol + chatId;

    public async Task<Unit> Do(Message message)
    {
        var privateChatId = message.Chat.Id;
        var type = message.Type;
        var telegramId = message.From!.Id;

        var isHandler = type switch
        {
            MessageType.Text when message.Text?.Contains(MazeGameInviteCommand) == true
                                  && message.Text.Split("=") is [_, var chatIdText]
                                  && long.TryParse(chatIdText, out var targetChatId) =>
                await SendMazeGame(privateChatId, targetChatId),
            _ => false
        };
        if (isHandler) return unit;

        var trySend = type switch
        {
            MessageType.Sticker => SendStickerFileId(privateChatId, message.Sticker!.FileId),
            MessageType.Text when message.Text == MeCommand => SendMe(telegramId, privateChatId),
            MessageType.Text when message.Text == GlobalAdminCommand =>
                await SendAdminButton(telegramId, privateChatId),
            MessageType.Text when message.Text == ProfileCommand => SendMenuButton(privateChatId),
            MessageType.Text when message.Text?.Contains(InventoryCommand) == true
                                  && message.Text.Split("=") is [_, var chatIdText]
                                  && long.TryParse(chatIdText, out var targetChatId) =>
                await SendInventory(privateChatId, targetChatId),
            MessageType.Text when message.Text == StatusCommand => SendStatus(privateChatId),
            MessageType.Text when message.Text is { } text && text.StartsWith(FastReplyHandler.StickerPrefix) =>
                SendSticker(privateChatId, text[FastReplyHandler.StickerPrefix.Length..]),
            MessageType.Text when message is { Text: not null, ReplyToMessage.Text: { } replyText }
                                  && replyText.Contains(LoreHandler.Tag) => loreHandler.Do(message).ToTryAsync(),
            MessageType.Text when message is { Text: not null, ReplyToMessage.Text: { } replyText }
                                  && replyText.Contains(FilterRecordHandler.Tag) =>
                filterRecordHandler.Do(message).ToTryAsync(),
            MessageType.Text when message is { Text: not null, ReplyToMessage.Text: { } replyText }
                                  && replyText.Contains(ChatConfigHandler.Tag) =>
                chatConfigHandler.Do(message).ToTryAsync(),
            MessageType.ChatShared when message.ChatShared is { ChatId: var sharedChatId } =>
                await SendProfile(sharedChatId, privateChatId),
            _ => TryAsync(botClient.SendMessage(privateChatId, "OK", cancellationToken: cancelToken.Token))
        };
        return await trySend.Match(
            message => Log.Information("Sent private {0} to {1}", message.Text?.Replace('\n', ' '), telegramId),
            ex => Log.Error(ex, "Failed to send private message to {0}", telegramId)
        );
    }

    private async Task<TryAsync<Message>> SendProfile(long chatId, long privateChatId)
    {
        var maybeMenuConfig = await chatConfigService.GetConfig(chatId, config => config.ProfileConfig);
        if (!maybeMenuConfig.TryGetSome(out var menuConfig))
        {
            Log.Warning("MenuConfig not found for chat {ChatId}", chatId);
            return TryAsync(botClient.SendMessage(privateChatId, "Меню для этого чата не настроено",
                cancellationToken: cancelToken.Token));
        }

        if (!await messageAssistance.IsAllowedChat(chatId)) return SendChatNotAllowed(privateChatId, menuConfig);
        var message = menuConfig.WelcomeMessage;
        var markup = await profileButtons.GetProfileMarkup(privateChatId, chatId);
        return TryAsync(botClient.SendMessage(privateChatId, message,
            replyMarkup: markup,
            cancellationToken: cancelToken.Token));
    }

    private TryAsync<Message> SendChatNotAllowed(long privateChatId, ProfileConfig profileConfig)
    {
        var message = profileConfig.ChatNotAllowed;
        return botClient.SendMessage(privateChatId, message, cancellationToken: cancelToken.Token).ToTryAsync();
    }

    private TryAsync<Message> SendMenuButton(long chatId)
    {
        var buttons = new ReplyKeyboardMarkup(new[]
        {
            KeyboardButton.WithRequestChat("Check profile", 1, false),
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };
        return TryAsync(botClient.SendMessage(chatId, GreetingsButtonText, replyMarkup: buttons,
            cancellationToken: cancelToken.Token));
    }

    private async Task<TryAsync<Message>> SendAdminButton(long telegramId, long chatId)
    {
        if (!appConfig.GlobalAdminConfig.AdminIds.Contains(telegramId))
        {
            Log.Information("User {TelegramId} is not a global admin", telegramId);
            return TryAsync(botClient.SendMessage(telegramId, "You are not a global admin",
                cancellationToken: cancelToken.Token));
        }

        var buttons = globalAdminButton.GetMarkup();

        return TryAsync(botClient.SendMessage(chatId, GreetingsButtonText, replyMarkup: buttons,
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

    private async Task<bool> SendMazeGame(long privateChatId, long chatId)
    {
        await mazeGameJoinCommandHandler.Do(chatId, privateChatId);
        return true;
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