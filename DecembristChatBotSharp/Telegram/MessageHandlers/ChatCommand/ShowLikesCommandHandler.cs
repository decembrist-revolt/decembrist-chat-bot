using System.Text;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class ShowLikesCommandHandler(
    AppConfig appConfig,
    CommandLockRepository lockRepository,
    MemberLikeRepository memberLikeRepository,
    BotClient botClient,
    MessageAssistance messageAssistance,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken
) : ICommandHandler
{
    public string Command => "/likes";
    public string Description => "Show top like users";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var chatId = parameters.ChatId;
        var messageId = parameters.MessageId;
        
        var locked = await lockRepository.TryAcquire(chatId, Command);
        if (!locked) return await messageAssistance.CommandNotReady(chatId, messageId, Command);
        
        Log.Information("Processing show likes command in chat {0}", chatId);

        var limit = appConfig.CommandConfig.LikeConfig.TopLikeMemberCount;
        var topLikeMembers = await memberLikeRepository.GetTopLikeMembers(chatId, limit);
        if (topLikeMembers.Count <= 0)
            return await SendLikes(chatId, appConfig.CommandConfig.LikeConfig.NoLikesMessage);

        var usernameCountChunks = await topLikeMembers.Chunk(5)
            .Map(chunk => chunk.Map(likeCount => FillUsername(chatId, likeCount)))
            .Map(Task.WhenAll)
            .AwaitAll();

        var usernameCounts = usernameCountChunks.Flatten();

        await SendLikes(chatId, BuildTopLikes(usernameCounts));
        expiredMessageRepository.QueueMessage(chatId, messageId);

        return unit;
    }

    private async Task<Unit> SendLikes(long chatId, string message) =>
        await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent top likes message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send top likes message to chat {0}", chatId),
            cancelToken.Token
        );

    private static string BuildTopLikes((string username, int Count)[] usernameCounts)
    {
        var idx = 1;
        var builder = new StringBuilder();
        builder.AppendLine("#  Username - Likes");
        foreach (var (username, count) in usernameCounts)
        {
            builder.AppendLine($"{idx++}. {username} - {count}");
        }

        return builder.ToString();
    }

    private async Task<(string username, int Count)> FillUsername(long chatId, LikeTelegramToLikeCount memberLikes)
    {
        var username = await botClient.GetChatMember(chatId, memberLikes.LikeTelegramId, cancelToken.Token)
            .ToTryAsync()
            .Map(chatMember => chatMember.GetUsername(false))
            .IfFail(ex =>
            {
                Log.Error(ex, "Failed to get username for telegramId {0}", memberLikes.LikeTelegramId);
                return $"Unknown, ID={memberLikes.LikeTelegramId}";
            });

        return (username, memberLikes.Count);
    }
}