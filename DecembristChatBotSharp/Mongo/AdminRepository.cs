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
    public Task<bool> IsAdmin(long telegramId)
    {
        var collection = GetCollection();

        return collection
            .Find(reply => reply.TelegramId == telegramId)
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find admin user with telegramId {0}", telegramId);
                return false;
            });
    }

    private IMongoCollection<AdminUser> GetCollection() => db.GetCollection<AdminUser>(nameof(AdminUser));
}