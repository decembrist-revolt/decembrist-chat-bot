using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Service;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class LoreRecordRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    private const string DefaultLoreContent = "Эта часть лора ещё не заполнена";

    public async Task<bool> AddLoreRecord(
        LoreRecord.CompositeId id,
        long telegramId,
        string? content = null,
        IMongoSession? session = null)
    {
        var collection = GetCollection();

        var update = Builders<LoreRecord>.Update
            .Set(x => x.Content, content ?? DefaultLoreContent)
            .AddToSet(x => x.AuthorIds, telegramId);

        var options = new UpdateOptions { IsUpsert = true };

        var filter = Builders<LoreRecord>.Filter.Eq(x => x.Id, id);
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

    public Task<bool> IsLoreRecordExist(LoreRecord.CompositeId id, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var filter = Builders<LoreRecord>.Filter.Eq(record => record.Id, id);

        var findTask = session.IsNull()
            ? collection.Find(filter)
            : collection.Find(session, filter);

        return findTask
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find lor record with id: {0}", id);
                return false;
            });
    }

    public async Task<Option<LoreRecord>> GetLoreRecord(LoreRecord.CompositeId id) => await GetCollection()
        .Find(m => m.Id == id)
        .SingleOrDefaultAsync(cancelToken.Token)
        .ToTryAsync()
        .Match(member => member.IsNull() ? None : Some(member),
            ex =>
            {
                Log.Error(ex, "Failed to get lore record: {0} in lore records db", id);
                return None;
            });

    public async Task<Option<List<string>>> GetLoreKeys(long chatId, int skip = 0)
    {
        if (skip < 0) return None;
        return await GetCollection()
            .Find(m => m.Id.ChatId == chatId)
            .SortBy(m => m.Id.Key)
            .Skip(skip)
            .Limit(ListService.ListRowLimit)
            .Project(record => record.Id.Key)
            .ToListAsync()
            .ToTryAsync()
            .Match(list => list.Count == 0 ? None : Some(list),
                ex =>
                {
                    Log.Error(ex, "Failed to get lore record for chat {0}", chatId);
                    return None;
                });
    }

    public async Task<Option<int>> GetKeysCount(long chatId) =>
        await GetCollection()
            .CountDocumentsAsync(m => m.Id.ChatId == chatId)
            .ToTryAsync()
            .Match(x => x == 0 ? None : Some((int)x),
                ex =>
                {
                    Log.Error(ex, "Failed get keys count in lore records db for chat {0}", chatId);
                    return None;
                });

    public async Task<bool> DeleteLogRecord(LoreRecord.CompositeId id) =>
        await GetCollection().DeleteOneAsync(m => m.Id == id, cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete lor record: {0} in lor records db", id);
                    return false;
                });

    private IMongoCollection<LoreRecord> GetCollection() => db.GetCollection<LoreRecord>(nameof(LoreRecord));
}