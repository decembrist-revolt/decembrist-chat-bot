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
    public async Task<bool> IsWhiteListMember(long telegramId)
    {
        var collection = GetCollection();

        return await collection
            .Find(member => member.TelegramId == telegramId)
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find user with telegramId {0}", telegramId);
                return false;
            });
    }

    private IMongoCollection<WhiteListMember> GetCollection() => 
        db.GetCollection<WhiteListMember>(nameof(WhiteListMember));
}
