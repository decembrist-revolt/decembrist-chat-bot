using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class MinionRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    private IMongoCollection<MinionRelation> GetCollection() =>
        db.GetCollection<MinionRelation>(nameof(MinionRelation));

    public async Task<Unit> EnsureIndexes()
    {
        var collection = GetCollection();
        var indexes = await (await collection.Indexes.ListAsync(cancelToken.Token)).ToListAsync(cancelToken.Token);
        
        // Index for master lookup
        const string masterIndex = $"{nameof(MinionRelation)}_MasterIndex_V1";
        if (indexes.All(index => index["name"] != masterIndex))
        {
            var masterIndexKey = Builders<MinionRelation>.IndexKeys.Ascending(x => x.MasterTelegramId);
            await collection.Indexes.CreateOneAsync(
                new CreateIndexModel<MinionRelation>(masterIndexKey, new CreateIndexOptions { Name = masterIndex }));
        }
        
        return unit;
    }

    public async Task<Option<MinionRelation>> GetMinionRelation(CompositeId minionId)
    {
        var collection = GetCollection();
        return await collection
            .Find(m => m.Id == minionId)
            .FirstOrDefaultAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(
                relation => relation != null ? Option<MinionRelation>.Some(relation) : Option<MinionRelation>.None,
                ex =>
                {
                    var (telegramId, chatId) = minionId;
                    Log.Error(ex, "Failed to get minion relation for {0} in chat {1}", telegramId, chatId);
                    return Option<MinionRelation>.None;
                });
    }

    public async Task<Option<MinionRelation>> GetMinionByMaster(long masterTelegramId, long chatId)
    {
        var collection = GetCollection();
        return await collection
            .Find(m => m.MasterTelegramId == masterTelegramId && m.Id.ChatId == chatId)
            .FirstOrDefaultAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(
                relation => relation != null ? Option<MinionRelation>.Some(relation) : Option<MinionRelation>.None,
                ex =>
                {
                    Log.Error(ex, "Failed to get minion by master {0} in chat {1}", masterTelegramId, chatId);
                    return Option<MinionRelation>.None;
                });
    }

    public async Task<bool> AddMinionRelation(MinionRelation relation, IMongoSession? session = null)
    {
        var collection = GetCollection();
        
        var insertTask = session.IsNull()
            ? collection.InsertOneAsync(relation, cancellationToken: cancelToken.Token)
            : collection.InsertOneAsync(session, relation, cancellationToken: cancelToken.Token);
        
        return await insertTask
            .ToTryAsync()
            .Match(
                _ => true,
                ex =>
                {
                    Log.Error(ex, "Failed to add minion relation {0} -> {1}", relation.Id, relation.MasterTelegramId);
                    return false;
                });
    }

    public async Task<bool> UpdateConfirmationMessageId(CompositeId minionId, int messageId, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var filter = Builders<MinionRelation>.Filter.Eq(m => m.Id, minionId);
        var update = Builders<MinionRelation>.Update.Set(m => m.ConfirmationMessageId, messageId);
        
        var updateTask = session.IsNull()
            ? collection.UpdateOneAsync(filter, update, cancellationToken: cancelToken.Token)
            : collection.UpdateOneAsync(session, filter, update, cancellationToken: cancelToken.Token);
        
        return await updateTask
            .ToTryAsync()
            .Match(
                result => result.IsAcknowledged && result.ModifiedCount > 0,
                ex =>
                {
                    var (telegramId, chatId) = minionId;
                    Log.Error(ex, "Failed to update confirmation message for minion {0} in chat {1}", telegramId, chatId);
                    return false;
                });
    }

    public async Task<bool> RemoveMinionRelation(CompositeId minionId, IMongoSession? session = null)
    {
        var collection = GetCollection();
        
        var deleteTask = session.IsNull()
            ? collection.DeleteOneAsync(m => m.Id == minionId, cancellationToken: cancelToken.Token)
            : collection.DeleteOneAsync(session, m => m.Id == minionId, cancellationToken: cancelToken.Token);
        
        return await deleteTask
            .ToTryAsync()
            .Match(
                result => result.IsAcknowledged && result.DeletedCount > 0,
                ex =>
                {
                    var (telegramId, chatId) = minionId;
                    Log.Error(ex, "Failed to remove minion relation for {0} in chat {1}", telegramId, chatId);
                    return false;
                });
    }

    public async Task<bool> RemoveMinionByMaster(long masterTelegramId, long chatId, IMongoSession? session = null)
    {
        var collection = GetCollection();
        
        var deleteTask = session.IsNull()
            ? collection.DeleteOneAsync(m => m.MasterTelegramId == masterTelegramId && m.Id.ChatId == chatId, 
                cancellationToken: cancelToken.Token)
            : collection.DeleteOneAsync(session, m => m.MasterTelegramId == masterTelegramId && m.Id.ChatId == chatId, 
                cancellationToken: cancelToken.Token);
        
        return await deleteTask
            .ToTryAsync()
            .Match(
                result => result.IsAcknowledged && result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to remove minion by master {0} in chat {1}", masterTelegramId, chatId);
                    return false;
                });
    }

    public async Task<Arr<long>> GetMinionIdsByChat(long chatId)
    {
        var collection = GetCollection();
        return await collection
            .Find(m => m.Id.ChatId == chatId)
            .ToListAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(
                relations => relations.Select(r => r.Id.TelegramId).ToArr(),
                ex =>
                {
                    Log.Error(ex, "Failed to get minions for chat {0}", chatId);
                    return Arr<long>.Empty;
                });
    }

    public async Task<Arr<MinionRelation>> GetAllMinionRelations()
    {
        var collection = GetCollection();
        return await collection
            .Find(FilterDefinition<MinionRelation>.Empty)
            .ToListAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(
                relations => relations.ToArr(),
                ex =>
                {
                    Log.Error(ex, "Failed to get all minion relations");
                    return Arr<MinionRelation>.Empty;
                });
    }
}
