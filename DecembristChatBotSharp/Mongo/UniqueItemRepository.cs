using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Service;
using Lamar;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class UniqueItemRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken)
    : IRepository
{
    public async Task<bool> ChangeOwnerUniqueItem(
        UniqueItem uniqueItem, IMongoSession? session = null)
    {
        var collection = GetCollection();

        var update = Builders<UniqueItem>.Update
            .Set(item => item.OwnerId, uniqueItem.OwnerId)
            .Set(item => item.GiveExpiration, uniqueItem.GiveExpiration);

        var filter = Builders<UniqueItem>.Filter.Eq(item => item.Id, uniqueItem.Id);
        var options = new UpdateOptions { IsUpsert = true };

        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, options, cancellationToken: cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, options, cancellationToken: cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && (result.UpsertedId != null || result.ModifiedCount == 1),
            ex =>
            {
                Log.Error(ex, "Failed to change unique item owner item:{0} in uniqueItemRepo", uniqueItem);
                return false;
            });
    }

    public Task<Option<DateTime>> GetExpirationTime(UniqueItem.CompositeId id)
    {
        var projection = Builders<UniqueItem>.Projection.Expression(x => x.GiveExpiration);
        return GetCollection().Find(x => x.Id == id).Project(projection).FirstAsync().ToTryAsync().Match(Some, ex =>
        {
            Log.Error(ex, "Failed to find unique item give expiration: {0} in uniqueItemRepo", id);
            return None;
        });
    }

    public Task<bool> IsGiveExpired(UniqueItem.CompositeId id, IMongoSession? session = null) =>
        GetCollection().AsQueryable(session)
            .AnyAsync(item => item.Id == id && item.GiveExpiration < DateTime.UtcNow)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find unique item : {0} in uniqueItemRepo", id);
                return false;
            });

    private IMongoCollection<UniqueItem> GetCollection() =>
        db.GetCollection<UniqueItem>(nameof(UniqueItem));
}