using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

public record LikeTelegramToLikeCount(long LikeTelegramId, int Count);

[Singleton]
public class MemberLikeRepository(
    AppConfig appConfig,
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    private const string UniqueMemberLikeIndex = $"{nameof(MemberLike)}_UniqueIndex_V1";

    public async Task<Unit> EnsureIndexes()
    {
        var collection = GetCollection();
        var indexes = await (await collection.Indexes.ListAsync(cancelToken.Token)).ToListAsync(cancelToken.Token);
        if (indexes.Any(index => index["name"] == UniqueMemberLikeIndex)) return unit;

        var indexBuilder = Builders<MemberLike>.IndexKeys
            .Ascending(member => member.Id.TelegramId)
            .Ascending(member => member.Id.ChatId)
            .Ascending(member => member.LikeTelegramId);
        var options = new CreateIndexOptions
        {
            Unique = true,
            Name = UniqueMemberLikeIndex
        };
        var indexModel = new CreateIndexModel<MemberLike>(indexBuilder, options);
        await collection.Indexes.CreateOneAsync(indexModel, cancellationToken: cancelToken.Token);

        return unit;
    }

    public async Task<List<LikeTelegramToLikeCount>> GetTopLikeMembers(long chatId)
    {
        var collection = GetCollection();
        var limit = appConfig.CommandConfig.TopLikeMemberCount;
        var pipeline = new EmptyPipelineDefinition<MemberLike>()
            .Match(member => member.Id.ChatId == chatId)
            .Group(member => member.LikeTelegramId,
                group => new LikeTelegramToLikeCount(group.Key, group.Count()))
            .Sort(Builders<LikeTelegramToLikeCount>.Sort.Descending(count => count.Count))
            .Limit(limit);

        var aggregateTask = collection.Aggregate(pipeline).ToListAsync(cancelToken.Token);
        return await TryAsync(aggregateTask).IfFail(ex =>
        {
            Log.Error(ex, "Failed to get top 10 like members for chat {0}", chatId);
            return [];
        }) ?? throw new Exception("Impossible aggregation null");
    }

    public Task<Unit> AddMemberLike(long telegramId, long chatId, long likeTelegramId, int value = 1)
    {
        var memberLikes = GetCollection();
        var memberLike = new MemberLike(
            new MemberLike.CompositeId(telegramId, chatId),
            likeTelegramId,
            value);
        return TryAsync(async () =>
        {
            await memberLikes.InsertOneAsync(memberLike, cancellationToken: cancelToken.Token);
            return unit;
        }).Match(identity, ex =>
        {
            Log.Error(ex, "Failed to add like from {0} to {1} in chat {2}", telegramId, likeTelegramId, chatId);
            return unit;
        });
    }

    public async Task<List<MemberLike>> FindMemberLikes(long telegramId, long chatId)
    {
        var memberLikes = GetCollection();
        var findTask = await TryAsync(memberLikes.Find(member => member.Id.TelegramId == telegramId
                                                                 && member.Id.ChatId == chatId)
            .ToListAsync(cancelToken.Token));

        return findTask.Match(identity, ex =>
        {
            Log.Error(ex, "Failed to find likes for {0} in chat {1}", telegramId, chatId);
            return [];
        });
    }

    public async Task<bool> RemoveMemberLike(long telegramId, long chatId, long likeTelegramId)
    {
        var memberLikes = GetCollection();
        var tryDelete = await TryAsync(memberLikes.DeleteOneAsync(member => member.Id.TelegramId == telegramId
                                                                            && member.Id.ChatId == chatId
                                                                            && member.LikeTelegramId == likeTelegramId,
            cancellationToken: cancelToken.Token));

        return tryDelete.Match(
            result => result.DeletedCount > 0,
            ex =>
            {
                Log.Error(ex, "Failed to remove like for {0} in chat {1}", telegramId, chatId);
                return false;
            });
    }

    private IMongoCollection<MemberLike> GetCollection() => db.GetCollection<MemberLike>(nameof(MemberLike));
}