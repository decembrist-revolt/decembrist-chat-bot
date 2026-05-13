using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class RestrictedInlineBotRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    public async Task<bool> IsRestricted(long chatId, string username) =>
        await GetCollection()
            .Find(record => record.Id == new RestrictedInlineBotId(chatId, username))
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find restricted inline bot {0} in chat {1}", username, chatId);
                return false;
            });

    public async Task<bool> AddRestrictedInlineBot(long chatId, string username)
    {
        var record = new RestrictedInlineBot(new RestrictedInlineBotId(chatId, username));
        var options = new ReplaceOptions { IsUpsert = true };

        return await GetCollection()
            .ReplaceOneAsync(stored => stored.Id == record.Id, record, options,
                cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.IsAcknowledged,
                ex =>
                {
                    Log.Error(ex, "Failed to add restricted inline bot {0} in chat {1}", username, chatId);
                    return false;
                });
    }

    public async Task<bool> DeleteRestrictedInlineBot(long chatId, string username) =>
        await GetCollection()
            .DeleteOneAsync(record => record.Id == new RestrictedInlineBotId(chatId, username), cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete restricted inline bot {0} in chat {1}", username, chatId);
                    return false;
                });

    private IMongoCollection<RestrictedInlineBot> GetCollection() =>
        db.GetCollection<RestrictedInlineBot>(nameof(RestrictedInlineBot));
}
