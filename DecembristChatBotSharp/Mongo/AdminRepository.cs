using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class AdminUserRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    public Task<bool> IsAdmin(CompositeId id)
    {
        var collection = GetCollection();

        return collection
            .Find(reply => reply.Id == id)
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find admin user with telegramId {0}", id);
                return false;
            });
    }

    public async Task<IReadOnlyList<AdminUser>> GetAdmins() =>
        await GetCollection()
            .Find(_ => true)
            .ToListAsync()
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to get all admin users");
                return [];
            });

    private IMongoCollection<AdminUser> GetCollection() => db.GetCollection<AdminUser>(nameof(AdminUser));
}