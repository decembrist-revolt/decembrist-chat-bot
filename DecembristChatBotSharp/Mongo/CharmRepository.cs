using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Service;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class CharmRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    private const string ExpireCharmMemberIndex = $"{nameof(CharmMember)}_ExpireIndex_V1";

    public async Task<Unit> EnsureIndexes()
    {
        var collection = GetCollection();
        var indexes = await (await collection.Indexes.ListAsync(cancelToken.Token)).ToListAsync(cancelToken.Token);
        if (indexes.Any(index => index["name"] == ExpireCharmMemberIndex)) return unit;

        var expireAtIndex = Builders<CharmMember>.IndexKeys.Ascending(x => x.ExpireAt);
        var options = new CreateIndexOptions
        {
            ExpireAfter = TimeSpan.Zero,
            Name = ExpireCharmMemberIndex
        };
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<CharmMember>(expireAtIndex, options));
        return unit;
    }

    public async Task<CharmResult> AddCharmMember(
        CharmMember member, IMongoSession? session = null)
    {
        var collection = GetCollection();

        if (await IsUserCharmed(member.Id, session)) return CharmResult.Duplicate;

        return await collection
            .InsertOneAsync(session, member, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ => CharmResult.Success,
                ex =>
                {
                    Log.Error(ex, "Failed to add charm to repository {0}", member.Id);
                    return CharmResult.Failed;
                });
    }

    public async Task<bool> IsUserCharmed(CompositeId id, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var filter = Builders<CharmMember>.Filter.Eq(member => member.Id, id);
        var findTask = session.IsNull()
            ? collection.Find(filter)
            : collection.Find(session, filter);
        return await findTask
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find charm user with id: {0}", id);
                return false;
            });
    }

    public async Task<Option<CharmMember>> GetCharmMember(CompositeId id) => await GetCollection()
        .Find(m => m.Id == id)
        .SingleOrDefaultAsync(cancelToken.Token)
        .ToTryAsync()
        .Match(member => member is null ? None : Some(member),
            ex =>
            {
                Log.Error(ex, "Failed to get user with telegramId {0} in charm db", id);
                return None;
            });

    public async Task<bool> DeleteCharmMember(CompositeId id, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var filter = Builders<CharmMember>.Filter.Eq(member => member.Id, id);
        var taskResult = session.IsNull()
            ? collection.DeleteOneAsync(filter, cancellationToken: cancelToken.Token)
            : collection.DeleteOneAsync(session, filter, cancellationToken: cancelToken.Token);
        return await taskResult
            .ToTryAsync().Match(result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete charm to user with telegramId {0} in charm collection", id);
                    return false;
                });
    }
    
    public async Task<bool> SetSecretMessageId(CompositeId id, int? messageId, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var update = Builders<CharmMember>.Update.Set(m => m.SecretMessageId, messageId);
        var taskResult = session.IsNull()
            ? collection.UpdateOneAsync(m => m.Id == id, update, cancellationToken: cancelToken.Token)
            : collection.UpdateOneAsync(session, m => m.Id == id, update, cancellationToken: cancelToken.Token);
        return await taskResult
            .ToTryAsync().Match(result => result.ModifiedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to set secret message id for user with telegramId {0} in charm collection", id);
                    return false;
                });
    }

    private IMongoCollection<CharmMember> GetCollection() =>
        db.GetCollection<CharmMember>(nameof(CharmMember));
}