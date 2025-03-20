using DecembristChatBotSharp.Mongo;
using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

public class LikeCommandHandler(
    AppConfig appConfig,
    CommandLockRepository lockRepository,
    MemberLikeRepository memberLikeRepository,
    BotClient botClient,
    MessageAssistance messageAssistance,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken
) : ICommandHandler
{
    public string Command => "/like";
    public string Description => "Reply with this command to give the user a like";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var chatId = parameters.ChatId;
        var telegramId = parameters.TelegramId;
        var likeToTelegramId = parameters.ReplyToTelegramId;
        var messageId = parameters.MessageId;

        if (likeToTelegramId.IsNone)
        {
            return await Array(
                SendLikeReceiverNotSet(chatId),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
        }

        var receiverTelegramId = likeToTelegramId.ValueUnsafe();
        if (telegramId == receiverTelegramId)
        {
            return await Array(
                SendSelfLikeMessage(chatId),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
        }

        var locked = await lockRepository.TryAcquire(chatId, Command, telegramId: telegramId);
        if (!locked) return await messageAssistance.CommandNotReady(chatId, messageId, Command);

        if (!await SetLike(telegramId, chatId, receiverTelegramId)) return unit;

        var sendLikeMessageTask = botClient.GetChatMember(chatId, receiverTelegramId, cancelToken.Token)
            .ToTryAsync()
            .Bind(chatMember => SendLikeMessage(chatId, chatMember).ToTryAsync())
            .Match(
                _ => Log.Information("Sent like message to chat {0}", chatId),
                ex => Log.Error(ex, "Failed to send like message to chat {0}", chatId)
            );

        return await Array(
            sendLikeMessageTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private async Task<bool> SetLike(long telegramId, long chatId, long receiverTelegramId)
    {
        var likes = await memberLikeRepository.FindMemberLikes(telegramId, chatId);
        if (likes.Any())
        {
            var like = likes.First();
            if (!await memberLikeRepository.RemoveMemberLike(telegramId, chatId, like.LikeTelegramId))
            {
                return false;
            }
        }

        await memberLikeRepository.AddMemberLike(telegramId, chatId, receiverTelegramId);

        Log.Information("Member {0} liked {1} in chat {2}", telegramId, receiverTelegramId, chatId);

        return true;
    }

    private async Task<Unit> SendLikeReceiverNotSet(long chatId)
    {
        var message = appConfig.CommandConfig.LikeReceiverNotSet;
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent like receiver not set message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send like receiver not set message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendSelfLikeMessage(long chatId)
    {
        var message = appConfig.CommandConfig.SelfLikeMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent self like message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send self like message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendLikeMessage(long chatId, ChatMember chatMember)
    {
        var username = chatMember.GetUsername();
        var message = string.Format(appConfig.CommandConfig.LikeMessage, username);
        await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);

        return unit;
    }
}