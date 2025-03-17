using System.Text;
using DecembristChatBotSharp.Mongo;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

public class ShowLikesCommandHandler(
    CommandLockRepository lockRepository,
    MemberLikeRepository memberLikeRepository,
    BotClient botClient
) : ICommandHandler
{
    public string Command => "/likes";
    public string Description => "Show top like users";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var chatId = parameters.ChatId;
        var locked = await lockRepository.TryAcquire(chatId, Command);
        if (!locked) return unit;
        
        Log.Information("Processing show likes command in chat {0}", chatId);
        
        await TryAsync(botClient.DeleteMessage(chatId, parameters.MessageId)).Match(
            _ => Log.Information("Deleted show likes message in chat {0}", chatId),
            ex => Log.Error(ex, "Failed to delete show likes message in chat {0}", chatId)
        );

        var topLikeMembers = await memberLikeRepository.GetTopLikeMembers(chatId);
        if (topLikeMembers.Count <= 0) return unit;

        var usernameCountChunks = await Task.WhenAll(topLikeMembers.Chunk(5)
            .Map(chunk => chunk.Map(likeCount => ToUsernameCount(chatId, likeCount)))
            .Map(Task.WhenAll));

        var usernameCounts = usernameCountChunks.Flatten();

        var idx = 1;
        var builder = new StringBuilder();
        builder.AppendLine("#  Username - Likes");
        foreach (var (username, count) in usernameCounts)
        {
            builder.AppendLine($"{idx++}. {username} - {count}");
        }

        return await TryAsync(botClient.SendMessage(chatId, builder.ToString())).Match(
            _ => Log.Information("Sent top likes message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send top likes message to chat {0}", chatId)
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