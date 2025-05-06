using System.Linq.Expressions;
using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class LorRecordRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    public async Task<bool> AddLorRecord(
        LorRecord.CompositeId id,
        long telegramId,
        string content = "Not filled in",
        IClientSessionHandle? session = null)
    {
        var collection = GetCollection();

        var update = Builders<LorRecord>.Update.Set(x => x.Content, content)
            .AddToSet(x => x.authorsId, telegramId);

        var options = new UpdateOptions { IsUpsert = true };

        var filter = Builders<LorRecord>.Filter.Eq(x => x.Id, id);
        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, options, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, options, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && (result.UpsertedId != null || result.ModifiedCount > 0),
            ex =>
            {
                Log.Error(ex, "Failed to add lor record id: {0}, author: {1}", id, telegramId);
                return false;
            });
    }

    public Task<bool> IsLorRecordExist(LorRecord.CompositeId id)
    {
        var collection = GetCollection();

        return collection
            .Find(record => record.Id == id)
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find lor record with id: {0}", id);
                return false;
            });
    }

    public async Task<Option<LorRecord>> GetLorRecord(LorRecord.CompositeId id) => await GetCollection()
        .Find(m => m.Id == id)
        .SingleOrDefaultAsync(cancelToken.Token)
        .ToTryAsync()
        .Match(member => member.IsDefault() ? None : Some(member),
            ex =>
            {
                Log.Error(ex, "Failed to get lor record: {0} in lor records db", id);
                return None;
            });

    public async Task<bool> DeleteLogRecord(LorRecord.CompositeId id) =>
        await GetCollection().DeleteOneAsync(m => m.Id == id, cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete lor record: {0} in lor records db", id);
                    return false;
                });

    private IMongoCollection<LorRecord> GetCollection() => db.GetCollection<LorRecord>(nameof(LorRecord));
}