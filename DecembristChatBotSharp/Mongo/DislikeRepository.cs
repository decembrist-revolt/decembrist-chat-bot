using System.Linq.Expressions;
using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class DislikeRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    private const string UniqueDislikeMemberIndex = $"{nameof(DislikeMember)}_UniqueIndex_V1";

    public async Task<Unit> EnsureIndexes()
    {
        var collection = GetCollection();
        var indexes = await (await collection.Indexes.ListAsync(cancelToken.Token)).ToListAsync(cancelToken.Token);
        if (indexes.Any(index => index["name"] == UniqueDislikeMemberIndex)) return unit;

        var indexBuilder = Builders<DislikeMember>.IndexKeys
            .Ascending(member => member.Id.TelegramId)
            .Ascending(member => member.Id.ChatId)
            .Ascending(member => member.DislikeTelegramId);
        var options = new CreateIndexOptions
        {
            Unique = true,
            Name = UniqueDislikeMemberIndex
        };
        var indexModel = new CreateIndexModel<DislikeMember>(indexBuilder, options);
        await collection.Indexes.CreateOneAsync(indexModel, cancellationToken: cancelToken.Token);

        return unit;
    }

    public async Task<DislikeResult> AddDislikeMember(DislikeMember member)
    {
        var collection = GetCollection();

        var tryFind = await collection.Find(m => m.Id == member.Id)
            .SingleOrDefaultAsync(cancelToken.Token)
            .ToTryOption();
        if (tryFind.IsSome()) return DislikeResult.Exist;

        return await collection
            .InsertOneAsync(member, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ => DislikeResult.Success,
                ex =>
                {
                    Log.Error(ex, "Failed to add dislike member {0}", member.Id);
                    return DislikeResult.Failed;
                });
    }

    public async Task<Option<DislikesResultGroup>> GetDislikeTopResults(long chatId,
        IClientSessionHandle? session = null)
    {
        var collection = GetCollection();
        var pipeline = new EmptyPipelineDefinition<DislikeMember>()
            .Match(m => m.Id.ChatId == chatId)
            .Group(m => m.DislikeTelegramId,
                g => new DislikesResultGroup
                    (g.Key, g.Select(x => x.Id.TelegramId).ToArray(), g.Count()))
            .Sort(Builders<DislikesResultGroup>.Sort.Descending(count => count.DislikersCount)).Limit(1);

        var cursor = session.IsNull()
            ? collection.Aggregate(pipeline, cancellationToken: cancelToken.Token)
            : collection.Aggregate(session, pipeline, cancellationToken: cancelToken.Token);
        return await cursor.FirstAsync().ToTryAsync().Match(
            Some,
            ex =>
            {
                Log.Error(ex, "");
                return None;
            });
    }

    public async Task<bool> RemoveAllInChat(long chatId, IClientSessionHandle? session = null)
    {
        var collection = GetCollection();
        Expression<Func<DislikeMember, bool>> filter = member => member.Id.ChatId == chatId;
        var deleteTask = session.IsNull()
            ? collection.DeleteManyAsync(filter, cancellationToken: cancelToken.Token)
            : collection.DeleteManyAsync(session, filter, cancellationToken: cancelToken.Token);

        return await deleteTask.ToTryAsync().Match(
            result => result.DeletedCount > 0,
            ex =>
            {
                Log.Error(ex, "Failed to remove dislikes for chat {0}", chatId);
                return false;
            });
    }

    public async Task<Arr<long>> GetChatIds() =>
        await GetCollection()
            .Distinct(member => member.Id.ChatId, member => true, cancellationToken: cancelToken.Token)
            .ToListAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(ListExtensions.ToArr, ex =>
            {
                Log.Error(ex, "Failed to get chat ids from dislikes repository");
                return [];
            });

    private IMongoCollection<DislikeMember> GetCollection() => db.GetCollection<DislikeMember>(nameof(DislikeMember));
}

public enum DislikeResult
{
    Exist,
    Failed,
    Success
}