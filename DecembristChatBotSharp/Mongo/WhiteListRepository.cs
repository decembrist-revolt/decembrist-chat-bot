using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class WhiteListRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository

{
    public async Task<bool> IsWhiteListMember(CompositeId id)
    {
        var collection = GetCollection();

        return await collection
            .Find(member => member.Id == id)
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find user with telegramId {0}", id);
                return false;
            });
    }

    private IMongoCollection<WhiteListMember> GetCollection() =>
        db.GetCollection<WhiteListMember>(nameof(WhiteListMember));
}