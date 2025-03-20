using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

public class FastReplyCommandHandler(
    AppConfig appConfig,
    AdminUserRepository adminUserRepository,
    FastReplyRepository fastReplyRepository,
    BotClient botClient,
    MessageAssistance messageAssistance,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken
) : ICommandHandler
{
    public string Command => "/fastreply@";
    public string Description => "Creates new fast reply option '/fastreply' for help";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var chatId = parameters.ChatId;
        var telegramId = parameters.TelegramId;
        var messageId = parameters.MessageId;

        if (parameters.Payload is not TextPayload { Text: var text }) return unit;
        if (!await adminUserRepository.IsAdmin(telegramId))
        {
            return await messageAssistance.SendAdminOnlyMessage(chatId, telegramId);
        }

        var args = text.Trim().Split("@").Skip(1).ToArray();
        if (args is not [var message, var reply])
        {
            return await Array(
                SendFastReplyHelp(chatId),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
        }

        var maybeFastReply = await CreateFastReply(chatId, message, reply, messageId);
        return await maybeFastReply.MatchAsync(fastReply => AddFastReply(chatId, fastReply), () => unit);
    }

    private async Task<Unit> AddFastReply(long chatId, FastReply fastReply)
    {
        var result = await fastReplyRepository.AddFastReply(fastReply);
        return result switch
        {
            FastReplyRepository.InsertResult.Duplicate => await SendDuplicateMessage(chatId, fastReply.Id.Message),
            _ => unit
        };
    }

    private async Task<Option<FastReply>> CreateFastReply(long chatId, string message, string reply, int messageId)
    {
        var messageType = FastReplyType.Text;
        if (message.StartsWith(FastReplyHandler.StickerPrefix))
        {
            var fileId = message[FastReplyHandler.StickerPrefix.Length..];
            if (!await CheckSticker(chatId, fileId, messageId)) return None;

            message = fileId;
            messageType = FastReplyType.Sticker;
        }

        var replyType = FastReplyType.Text;
        if (reply.StartsWith(FastReplyHandler.StickerPrefix))
        {
            var fileId = reply[FastReplyHandler.StickerPrefix.Length..];
            if (!await CheckSticker(chatId, fileId, messageId)) return None;

            reply = fileId;
            replyType = FastReplyType.Sticker;
        }

        return new FastReply((chatId, message), reply, messageType, replyType);
    }

    private async Task<bool> CheckSticker(long chatId, string fileId, int messageId)
    {
        if (await botClient.SendSticker(chatId, fileId).ToTryAsync().IsFail())
        {
            await Array(
                messageAssistance.SendStickerNotFound(chatId, fileId),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();

            return false;
        }

        return true;
    }

    private async Task<Unit> SendFastReplyHelp(long chatId)
    {
        var message = appConfig.CommandConfig.FastReplyHelpMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent fast reply help message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send fast reply help message to chat {0}", chatId),
            cancelToken.Token);
    }


    private async Task<Unit> SendDuplicateMessage(long chatId, string text)
    {
        var message = string.Format(appConfig.CommandConfig.FastReplyDuplicateMessage, text);
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent fast reply duplicate message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send fast reply duplicate message to chat {0}", chatId),
            cancelToken.Token);
    }
}