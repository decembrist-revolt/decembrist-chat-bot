using System.Runtime.CompilerServices;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Telegram;

[Singleton]
public class MessageAssistance(
    AppConfig appConfig,
    BotClient botClient,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken)
{
    public async Task<Unit> CommandNotReady(
        long chatId,
        int commandMessageId,
        string command) => await Array(
        SendCommandNotReady(chatId, command),
        DeleteCommandMessage(chatId, commandMessageId, command)).WhenAll();

    public async Task<Unit> SendCommandNotReady(long chatId, string command)
    {
        var interval = appConfig.CommandConfig.CommandIntervalSeconds;
        var message = string.Format(appConfig.CommandConfig.CommandNotReady, command, interval);
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent command not ready message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send command not ready message to chat {0}", chatId),
            cancelToken.Token);
    }

    public async Task<Unit> DeleteCommandMessage(long chatId, int messageId, string command) =>
        await botClient.DeleteMessageAndLog(chatId, messageId,
            () => Log.Information("Deleted {0} message in chat {1}", command, chatId),
            ex => Log.Error(ex, "Failed to delete like message in chat {0}", chatId),
            cancelToken.Token);

    public async Task<Unit> SendAdminOnlyMessage(long chatId, long telegramId) =>
        await botClient.SendMessageAndLog(chatId, appConfig.CommandConfig.AdminOnlyMessage,
            message =>
            {
                Log.Information("Sent admin only message to {0} chat {1}", telegramId, chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send admin only message to {0} chat {1}", telegramId, chatId),
            cancelToken.Token);

    public async Task<Unit> SendStickerNotFound(long chatId, string fileId)
    {
        var stickerMessage = FastReplyHandler.StickerPrefix + fileId;
        var message = string.Format(appConfig.CommandConfig.StickerNotFoundMessage, stickerMessage);
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent sticker not found message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send sticker not found message to chat {0}", chatId),
            cancelToken.Token);
    }

    public async Task<Unit> SendNoItems(long chatId) =>
        await botClient.SendMessageAndLog(chatId, appConfig.ItemConfig.NoItemsMessage,
            message =>
            {
                Log.Information("Sent no items message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send no items message to chat {0}", chatId),
            cancelToken.Token);

    public async Task<Unit> SendGetItemMessage(long chatId, string username, MemberItemType item, int count = 1)
    {
        var message = string.Format(appConfig.ItemConfig.GetItemMessage, username, item);
        message += count switch
        {
            > 1 => "\n\n" + string.Format(appConfig.ItemConfig.MultipleItemMessage, count),
            0 when item == MemberItemType.Amulet => "\n\n" + string.Format(appConfig.ItemConfig.AmuletBrokenMessage),
            _ => string.Empty
        };

        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                var expireAt = DateTime.UtcNow.AddMinutes(appConfig.ItemConfig.BoxMessageExpiration);
                Log.Information("Sent get item message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId, expireAt);
            },
            ex => Log.Error(ex, "Failed to send get item message to chat {0}", chatId),
            cancelToken.Token);
    }

    public async Task<Unit> SendInviteToDirect(long chatId, string url, string message)
    {
        var replyMarkup = new InlineKeyboardMarkup(
            InlineKeyboardButton.WithUrl(appConfig.CommandConfig.InviteToDirectMessage, url));
        return await botClient.SendMessage(
                chatId, message, replyMarkup: replyMarkup, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(message =>
                {
                    Log.Information("Sent invite to direct message to chat {0}", chatId);
                    expiredMessageRepository.QueueMessage(chatId, message.MessageId);
                },
                ex => Log.Error(ex, "Failed to send invite to direct message to chat {0}", chatId));
    }

    public async Task<Unit> SendAmuletMessage(long chatId, long receiverId, string commandName)
    {
        var username = await botClient.GetUsername(chatId, receiverId, cancelToken.Token)
            .ToAsync()
            .IfNone(receiverId.ToString);
        var message = string.Format(appConfig.AmuletConfig.AmuletBreaksMessage, username, commandName);
        return await SendCommandResponse(chatId, message, commandName,
            DateTime.UtcNow.AddMinutes(appConfig.AmuletConfig.MessageExpirationMinutes));
    }

    public async Task<Unit> SendMessage(
        long chatId,
        string message,
        string commandName,
        ReplyMarkup? replyMarkup = null,
        ParseMode parseMode = ParseMode.None,
        [CallerMemberName] string callerName = "UnknownCaller") =>
        await botClient.SendMessageAndLog(chatId, message, parseMode,
            message => Log.Information("Sent response to command:'{0}' from {1} to chat {2}", commandName, callerName,
                chatId),
            ex => Log.Error(ex, "Failed to send response to command: {0} from {1} to chat {2}",
                commandName, callerName, chatId),
            cancelToken.Token, replyMarkup);

    public async Task<Unit> SendCommandResponse(
        long chatId,
        string message,
        string commandName,
        DateTime? expirationDate = null,
        ReplyMarkup? replyMarkup = null,
        ParseMode parseMode = ParseMode.None,
        [CallerMemberName] string callerName = "UnknownCaller") =>
        await botClient.SendMessageAndLog(chatId, message, parseMode,
            message =>
            {
                Log.Information("Sent response to command:'{0}' from {1} to chat {2}", commandName, callerName, chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId, expirationDate);
            },
            ex => Log.Error(ex, "Failed to send response to command: {0} from {1} to chat {2}",
                commandName, callerName, chatId),
            cancelToken.Token, replyMarkup);

    public Task<Unit> TryEditMarkdownMessage(
        long chatId,
        int messageId,
        string message,
        string commandName,
        InlineKeyboardMarkup? replyMarkup = null,
        [CallerMemberName] string callerName = "UnknownCaller") =>
        botClient.EditMessageText(chatId, messageId, message,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancelToken.Token,
                replyMarkup: replyMarkup
            )
            .ToTryAsync()
            .Match(_ =>
                {
                    Log.Information("Edited markdown message to command {0}: from: {1}, message: {2}, chat:{3}",
                        commandName, callerName, messageId, chatId);
                    return Task.FromResult(unit);
                },
                async ex =>
                {
                    Log.Error(
                        "Failed to edit markdown message to command {0}: from {1}, message:{2} to chat {3}, \n\r Exception: {4}",
                        commandName, callerName, messageId, chatId, ex);
                    Log.Information("Trying to edit message without markdown");
                    return await EditMessageAndLog(chatId, messageId, message, commandName, replyMarkup);
                }
            );

    public async Task<Unit> EditMessageAndLog(
        long chatId,
        int messageId,
        string message,
        string commandName,
        InlineKeyboardMarkup? replyMarkup = null,
        ParseMode parseMode = ParseMode.None,
        [CallerMemberName] string callerName = "UnknownCaller") =>
        await botClient.EditMessageAndLog(chatId, messageId, message,
            _ =>
                Log.Information("Edit message to command {0}: from: {1}, message: {2}, chat:{3}",
                    commandName, callerName, messageId, chatId),
            ex =>
                Log.Error(ex, "Failed to edit message to command {0}: from {1}, message:{2} to chat {3}",
                    commandName, callerName, messageId, chatId),
            cancelToken: cancelToken.Token,
            parseMode: parseMode,
            replyMarkup
        );

    public async Task<Unit> EditMessageMediaAndLog(
        long chatId,
        int messageId,
        InputMedia inputMedia,
        string commandName,
        InlineKeyboardMarkup? replyMarkup = null,
        ParseMode parseMode = ParseMode.None,
        [CallerMemberName] string callerName = "UnknownCaller") =>
        await botClient.EditMessageMediaAndLog(chatId, messageId, inputMedia,
            _ =>
                Log.Information("Edit message media to command {0}: from: {1}, message: {2}, chat:{3}",
                    commandName, callerName, messageId, chatId),
            ex =>
                Log.Error(ex, "Failed to edit message media to command {0}: from {1}, message:{2} to chat {3}",
                    commandName, callerName, messageId, chatId),
            cancelToken: cancelToken.Token,
            parseMode: parseMode,
            replyMarkup
        );

    public async Task<Unit> EditProfileMessage(
        long telegramId,
        long chatId,
        int messageId,
        InlineKeyboardMarkup replyMarkup,
        string text,
        string commandName,
        ParseMode parseMode = ParseMode.None,
        [CallerMemberName] string callerName = "UnknownCaller")
    {
        var chatTitle = await botClient.GetChatTitleOrId(chatId, cancelToken.Token);
        var message = string.Format(appConfig.MenuConfig.ProfileTitle, chatTitle, text);
        return await EditMessageAndLog(
            telegramId, messageId, message, commandName, replyMarkup: replyMarkup, parseMode, callerName: callerName);
    }

    public async Task<Unit> AnswerCallbackQuery(
        string queryId,
        long chatId,
        string prefix,
        string? message = null,
        bool showAlert = false,
        [CallerMemberName] string callerName = "UnknownCaller") =>
        await botClient.AnswerCallbackAndLog(queryId, () =>
                Log.Information("Answer callback to query {0}: from: {1}, queryId:{2}, chat:{3}",
                    prefix, callerName, queryId, chatId),
            ex => Log.Error(ex, "Failed to answer callback to query {0}: from {1}, queryId:{2} to chat {3}",
                prefix, callerName, queryId, chatId),
            cancelToken: cancelToken.Token, message: message, showAlert: showAlert);

    public async Task<Unit> SendAddPremiumMessage(long chatId, long telegramId, int days)
    {
        var maybeUsername = await botClient.GetUsername(chatId, telegramId, cancelToken.Token);
        var username = maybeUsername.IfNone("Anonymous");
        var message = string.Format(appConfig.CommandConfig.PremiumConfig.AddPremiumMessage, username, days);
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent add premium message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send add premium message to chat {0}", chatId),
            cancelToken.Token);
    }

    public async Task<Unit> SendUpdatePremiumMessage(long chatId, long telegramId, int days)
    {
        var maybeUsername = await botClient.GetUsername(chatId, telegramId, cancelToken.Token);
        var username = maybeUsername.IfNone("Anonymous");
        var message = string.Format(appConfig.CommandConfig.PremiumConfig.AddPremiumMessage, username, days);
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent add premium message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send add premium message to chat {0}", chatId),
            cancelToken.Token);
    }

    public bool IsAllowedChat(long chatId) => appConfig.AllowedChatConfig.AllowedChatIds?.Contains(chatId) == true;
}