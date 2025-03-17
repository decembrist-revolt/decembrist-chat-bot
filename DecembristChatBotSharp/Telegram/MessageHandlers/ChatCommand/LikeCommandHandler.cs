using DecembristChatBotSharp.Mongo;
using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

public class LikeCommandHandler(
    AppConfig appConfig,
    CommandLockRepository lockRepository,
    MemberLikeRepository memberLikeRepository,
    BotClient botClient,
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
        var locked = await lockRepository.TryAcquire(chatId, Command, telegramId: telegramId);
        if (!locked)
        {
            return await Task.WhenAll(
                SendCommandNotReady(chatId),
                DeleteCommandMessage(chatId, parameters.MessageId)).UnitTask();
        }

        if (likeToTelegramId.IsNone)
        {
            return await Task.WhenAll(
                SendLikeReceiverNotSet(chatId),
                DeleteCommandMessage(chatId, parameters.MessageId)).UnitTask();
        }

        var receiverTelegramId = likeToTelegramId.ValueUnsafe();
        if (!await SetLike(telegramId, chatId, receiverTelegramId)) return unit;

        var trySend = botClient.GetChatMember(chatId, receiverTelegramId, cancelToken.Token)
            .ToTryAsync()
            .Map(chatMember =>
            {
                var message = string.Format(appConfig.CommandConfig.LikeMessage, chatMember.User.FirstName);
                return botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);
            }).Match(
                _ => Log.Information("Sent like message to chat {0}", chatId),
                ex => Log.Error(ex, "Failed to send like message to chat {0}", chatId)
            );
        
        return await Task.WhenAll(
            trySend,
            DeleteCommandMessage(chatId, parameters.MessageId)).UnitTask();
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
        await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent like receiver not set message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send like receiver not set message to chat {0}", chatId),
            cancelToken.Token);

        return unit;
    }

    private async Task<Unit> SendCommandNotReady(long chatId)
    {
        var message = appConfig.CommandConfig.CommandNotReady;
        await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent command not ready message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send command not ready message to chat {0}", chatId),
            cancelToken.Token);

        return unit;
    }

    private async Task<Unit> DeleteCommandMessage(long chatId, int messageId) =>
        await botClient.DeleteMessageAndLog(chatId, messageId,
            () => Log.Information("Deleted like message in chat {0}", chatId),
            ex => Log.Error(ex, "Failed to delete like message in chat {0}", chatId));

    private async Task<(string username, int Count)> ToUsernameCount(long chatId, LikeTelegramToLikeCount memberLikes)
    {
        var username = await TryAsync(botClient.GetChatMember(chatId, memberLikes.LikeTelegramId))
            .Map(chatMember => chatMember.User.Username ?? chatMember.User.FirstName)
            .IfFail(ex =>
            {
                Log.Error(ex, "Failed to get username for telegramId {0}", memberLikes.LikeTelegramId);
                return $"Unknown, ID={memberLikes.LikeTelegramId}";
            });

        return (username, memberLikes.Count);
    }
}