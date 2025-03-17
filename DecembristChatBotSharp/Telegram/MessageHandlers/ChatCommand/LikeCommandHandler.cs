using DecembristChatBotSharp.Mongo;
using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

public class LikeCommandHandler(
    CommandLockRepository lockRepository,
    MemberLikeRepository memberLikeRepository,
    BotClient botClient
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
        if (!locked) return unit;
        
        await TryAsync(botClient.DeleteMessage(chatId, parameters.MessageId)).Match(
            _ => Log.Information("Deleted like message in chat {0}", chatId),
            ex => Log.Error(ex, "Failed to delete like message in chat {0}", chatId)
        );

        if (likeToTelegramId.IsNone) return unit;

        var likes = await memberLikeRepository.FindMemberLikes(telegramId, chatId);
        if (likes.Any())
        {
            var like = likes.First();
            if (!await memberLikeRepository.RemoveMemberLike(telegramId, chatId, like.LikeTelegramId)) return unit;
        }
        await memberLikeRepository.AddMemberLike(telegramId, chatId, likeToTelegramId.ValueUnsafe());

        Log.Information("Member {0} liked {1} in chat {2}", telegramId, likeToTelegramId, chatId);

        return await TryAsync(botClient.DeleteMessage(chatId, parameters.MessageId)).Match(
            _ => Log.Information("Deleted like message in chat {0}", chatId),
            ex => Log.Error(ex, "Failed to delete like message in chat {0}", chatId)
        );
    }

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