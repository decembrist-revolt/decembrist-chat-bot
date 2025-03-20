using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Serilog;

namespace DecembristChatBotSharp.Telegram;

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
            ex => Log.Error(ex, "Failed to delete like message in chat {0}", chatId));
    
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
}