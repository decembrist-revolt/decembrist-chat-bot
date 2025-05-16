using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class CallbackRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    private const string ExpireCallbackPermissionIndex = $"{nameof(CallbackPermission)}_ExpireIndex_V1";

    public async Task<Unit> EnsureIndexes()
    {
        var collection = GetCollection();
        var indexes = await (await collection.Indexes.ListAsync(cancelToken.Token)).ToListAsync(cancelToken.Token);
        if (indexes.Any(index => index["name"] == ExpireCallbackPermissionIndex)) return unit;

        var expireAtIndex = Builders<CallbackPermission>.IndexKeys.Ascending(x => x.ExpireAt);
        var options = new CreateIndexOptions
        {
            ExpireAfter = TimeSpan.Zero,
            Name = ExpireCallbackPermissionIndex
        };
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<CallbackPermission>(expireAtIndex, options));
        return unit;
    }

    public async Task<bool> AddCallbackPermission(CallbackPermission permission, IMongoSession? session = null)
    {
        var collection = GetCollection();

        var update = Builders<CallbackPermission>.Update
            .Set(x => x.ExpireAt, permission.ExpireAt);

        var options = new UpdateOptions { IsUpsert = true };

        var filter = Builders<CallbackPermission>.Filter.Eq(x => x.Id, permission.Id);
        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, options, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, options, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && (result.UpsertedId != null || result.ModifiedCount > 0),
            ex =>
            {
                Log.Error(ex, "Failed to add callback permission id: {0}", permission.Id);
                return false;
            });
    }

    public Task<bool> HasPermission(CallbackPermission.CompositeId id)
    {
        var collection = GetCollection();

        return collection
            .Find(permission => permission.Id == id)
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find permission with id: {0}", id);
                return false;
            });
    }

    private IMongoCollection<CallbackPermission> GetCollection() =>
        db.GetCollection<CallbackPermission>(nameof(CallbackPermission));
}