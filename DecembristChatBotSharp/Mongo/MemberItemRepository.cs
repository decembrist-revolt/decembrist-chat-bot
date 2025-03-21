using System.Linq.Expressions;
using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class MemberItemRepository(MongoDatabase db, CancellationTokenSource cancelToken) : IRepository
{
    /// <returns>True if item was added</returns>
    public async Task<bool> AddMemberItem(
        long telegramId,
        long chatId,
        MemberItemType type,
        IClientSessionHandle? session = null)
    {
        var collection = GetCollection();

        var id = new MemberItem.CompositeId(telegramId, chatId, type);
        var update = Builders<MemberItem>.Update.Inc(item => item.Count, 1);
        var options = new UpdateOptions { IsUpsert = true };

        Expression<Func<MemberItem,bool>> findExpr = item => item.Id == id;
        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, findExpr, update, options, cancelToken.Token)
            : collection.UpdateOneAsync(findExpr, update, options, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && (result.UpsertedId != null || result.ModifiedCount == 1),
            ex =>
            {
                Log.Error(ex, "Failed to add item {0} to {1} in chat {2}", type, telegramId, chatId);
                return false;
            });
    }
    
    /// <returns>True if item was removed, false if item was not found or count was 0</returns>
    public async Task<bool> RemoveMemberItem(
        long telegramId,
        long chatId,
        MemberItemType type,
        IClientSessionHandle? session = null)
    {
        var collection = GetCollection();

        var id = new MemberItem.CompositeId(telegramId, chatId, type);

        var update = Builders<MemberItem>.Update.Inc(item => item.Count, -1);

        Expression<Func<MemberItem,bool>> findExpr = item => item.Id == id && item.Count > 0;
        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, findExpr, update, cancellationToken: cancelToken.Token)
            : collection.UpdateOneAsync(findExpr, update, cancellationToken: cancelToken.Token);
        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && result.ModifiedCount == 1,
            ex =>
            {
                Log.Error(ex, "Failed to remove item {0} from {1} in chat {2}", type, telegramId, chatId);
                return false;
            });
    }

    private IMongoCollection<MemberItem> GetCollection() => db.GetCollection<MemberItem>(nameof(MemberItem));
}