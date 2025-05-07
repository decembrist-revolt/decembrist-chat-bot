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

    private IMongoCollection<LoreUser> GetCollection() => db.GetCollection<LoreUser>(nameof(LoreUser));
}