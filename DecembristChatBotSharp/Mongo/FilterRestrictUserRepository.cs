using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class FilterRestrictUserRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    public async Task<bool> AddUser(FilterRestrictUser member)
    {
        var collection = GetCollection();
        return await collection.InsertOneAsync(member, cancellationToken: cancelToken.Token).ToTryAsync()
            .Match(_ =>
                {
                    Log.Information("Added restrict filter member {0}", member.Id);
                    return true;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to add restrict filter member {0}", member.Id);
                    return false;
                });
    }

    public async Task<IReadOnlyList<FilterRestrictUser>> GetUsersExpired() =>
        await GetCollection()
            .Find(user => user.Expired < DateTime.UtcNow)
            .ToListAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to get expired filter restrict users");
                return [];
            });

    public async Task<long> DeleteUsers(IReadOnlyList<FilterRestrictUser> users)
    {
        if (users.Count == 0) return 0;

        var ids = users.Select(u => u.Id).ToList();
        var filter = Builders<FilterRestrictUser>.Filter.In(u => u.Id, ids);

        return await GetCollection()
            .DeleteManyAsync(filter, cancelToken.Token)
            .ToTryAsync()
            .Match(result => result.DeletedCount, ex =>
            {
                Log.Error(ex, "Failed to delete provided filter restrict users");
                return 0;
            });
    }

    public async Task<bool> DeleteUser(CompositeId id) =>
        await GetCollection()
            .DeleteOneAsync(u => u.Id == id, cancelToken.Token)
            .ToTryAsync()
            .Match(x => x.DeletedCount > 0, ex =>
            {
                Log.Error(ex, "Failed to delete provided filter restrict users");
                return false;
            });

    private IMongoCollection<FilterRestrictUser> GetCollection() =>
        db.GetCollection<FilterRestrictUser>(nameof(FilterRestrictUser));
}