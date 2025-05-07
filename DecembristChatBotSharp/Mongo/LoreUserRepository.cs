using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class LoreUserRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    public Task<bool> IsLoreUser(CompositeId id)
    {
        var collection = GetCollection();

        return collection
            .Find(reply => reply.Id == id)
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find lor user with id: {0}", id);
                return false;
            });
    }

    public async Task<bool> AddLoreUser(LoreUser user)
    {
        var collection = GetCollection();

        var isExist = await collection
            .Find(lorUser => lorUser.Id == user.Id)
            .AnyAsync(cancelToken.Token);
        if (isExist) return false;

        return await collection.InsertOneAsync(user, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(_ => true,
                ex =>
                {
                    Log.Error(ex, "Failed to add lor user {0}", user.Id);
                    return false;
                });
    }

    public async Task<bool> DeleteLoreUser(CompositeId id) =>
        await GetCollection().DeleteOneAsync(m => m.Id == id, cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete lor user with id {0} in log users db", id);
                    return false;
                });

    private IMongoCollection<LoreUser> GetCollection() => db.GetCollection<LoreUser>(nameof(LoreUser));
}