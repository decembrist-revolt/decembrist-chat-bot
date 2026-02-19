using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class WhiteListRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository

{
    public async Task<bool> IsWhiteListMember(CompositeId id) =>
        await GetCollection().AsQueryable()
            .AnyAsync(member => member.Id == id, cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find user with telegramId {0} in white list", id);
                return false;
            });

    public async Task<bool> AddWhiteListMember(WhiteListMember member, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var query = session != null
            ? collection.InsertOneAsync(session, member, cancellationToken: cancelToken.Token)
            : collection.InsertOneAsync(member, cancellationToken: cancelToken.Token);
        return await query.ToTryAsync()
            .Match(_ =>
                {
                    Log.Information("Added white list member {0}", member.Id);
                    return true;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to add white list member {0}", member.Id);
                    return false;
                });
    }

    public async Task<bool> DeleteWhiteListMember(CompositeId id) =>
        await GetCollection().DeleteOneAsync(m => m.Id == id, cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete user with telegramId {0} in whitelist db", id);
                    return false;
                });

    private IMongoCollection<WhiteListMember> GetCollection() =>
        db.GetCollection<WhiteListMember>(nameof(WhiteListMember));
}