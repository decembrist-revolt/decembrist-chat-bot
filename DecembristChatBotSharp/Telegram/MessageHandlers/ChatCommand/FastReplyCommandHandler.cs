using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class FastReplyCommandHandler(
    AppConfig appConfig,
    AdminUserRepository adminUserRepository,
    FastReplyRepository fastReplyRepository,
    MemberItemService memberItemService,
    BotClient botClient,
    MessageAssistance messageAssistance,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken
) : ICommandHandler
{
    public const string CommandKey = "/fastreply";

    public string Command => CommandKey;
    public string Description => "Creates new fast reply option '/fastreply' for help";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var args = text.Trim().Split("@").Skip(1).ToArray();
        if (args is not [var message, var reply])
        {
            return await Array(
                SendFastReplyHelp(chatId),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
        }

        var isAdmin = await adminUserRepository.IsAdmin((telegramId, chatId));

        if (isAdmin && message == ChatCommandHandler.DeleteSubcommand)
        {
            return await DeleteFastReply(chatId, reply, messageId);
        }

        var maybeFastReply = await CreateFastReply(chatId, message, reply, messageId);

        return await maybeFastReply.MapAsync(async fastReply =>
        {
            var result = await memberItemService.UseFastReply(chatId, telegramId, fastReply, isAdmin);
            return result switch
            {
                UseFastReplyResult.NoItems => await messageAssistance.SendNoItems(chatId),
                UseFastReplyResult.Duplicate => await Array(
                    expiredMessageRepository.QueueMessage(chatId, messageId),
                    SendDuplicateMessage(chatId, fastReply.Id.Message)).WhenAll(),
                UseFastReplyResult.Success => await SendNewFastReply(chatId, fastReply.Id.Message, fastReply.Reply),
                _ => unit
            };
        }).IfSome(identity);
    }

    private async Task<Unit> DeleteFastReply(long chatId, string message, int messageId)
    {
        if (message.StartsWith(FastReplyHandler.StickerPrefix))
        {
            message = message[FastReplyHandler.StickerPrefix.Length..];
        }

        var id = (chatId, message);
        var isDeleted = await fastReplyRepository.DeleteFastReply(id);
        if (isDeleted) Log.Information("Successfully deleted fast reply: {0}", id);
        else Log.Error("Failed to delete fast reply: {0}", id);

        return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
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
        else message = message.ToLowerInvariant();

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

    private async Task<Unit> SendNewFastReply(long chatId, string message, string reply)
    {
        Log.Information("New fast reply {0} -> {1} in chat {2}", message, reply, chatId);

        var replyMessage = string.Format(appConfig.CommandConfig.NewFastReplyMessage, message, reply);
        return await messageAssistance.SendCommandResponse(chatId, replyMessage, Command);
    }

    private async Task<Unit> SendFastReplyHelp(long chatId)
    {
        var message = appConfig.CommandConfig.FastReplyHelpMessage;
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }


    private async Task<Unit> SendDuplicateMessage(long chatId, string text)
    {
        var message = string.Format(appConfig.CommandConfig.FastReplyDuplicateMessage, text);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }
}