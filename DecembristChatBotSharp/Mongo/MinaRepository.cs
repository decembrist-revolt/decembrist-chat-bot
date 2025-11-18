using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Service;
using Lamar;
using LanguageExt.Common;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class MinaRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    private const string ExpireMineTriggerIndex = $"{nameof(MineTrigger)}_ExpireIndex_V1";
    
    public async Task<Unit> EnsureIndexes()
    {
        var collection = GetCollection();
        var indexes = await (await collection.Indexes.ListAsync(cancelToken.Token)).ToListAsync(cancelToken.Token);
        if (indexes.Any(index => index["name"] == ExpireMineTriggerIndex)) return unit;
        
        var expireAtIndex = Builders<MineTrigger>.IndexKeys.Ascending(x => x.ExpireAt);
        var options = new CreateIndexOptions
        {
            ExpireAfter = TimeSpan.Zero,
            Name = ExpireMineTriggerIndex
        };
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<MineTrigger>(expireAtIndex, options));
        return unit;
    }

    public async Task<MinaResult> AddMineTrigger(
        MineTrigger trigger, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var isDuplicateResult = await HasMineTrigger(trigger.Id, session);
        if (isDuplicateResult.IsLeft) return MinaResult.Failed;
        if (isDuplicateResult.IfLeftThrow()) return MinaResult.Duplicate;

        return await collection
            .InsertOneAsync(session, trigger, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ => MinaResult.Success,
                ex =>
                {
                    Log.Error(ex, "Failed to add mine trigger {0}", trigger.Id);
                    return MinaResult.Failed;
                });
    }

    public async Task<Either<Error, bool>> HasMineTrigger(MineTrigger.CompositeId id, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var filter = Builders<MineTrigger>.Filter.Eq(trigger => trigger.Id, id);
        var findTask = session.IsNull()
            ? collection.Find(filter)
            : collection.Find(session, filter);
        return await findTask
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .ToEither()
            .MapLeft(ex =>
            {
                Log.Error(ex, "Failed to find mine trigger with id: {0}", id);
                return ex;
            });
    }

    public async Task<Option<MineTrigger>> FindMineTrigger(long chatId, string message)
    {
        var collection = GetCollection();
        var messageLower = message.ToLowerInvariant();
        
        return await collection
            .Find(m => m.Id.ChatId == chatId)
            .ToListAsync(cancelToken.Token)
            .ToTryOption()
            .Match(
                triggers =>
                {
                    var matchedTrigger = triggers.FirstOrDefault(t => 
                        messageLower.Contains(t.Id.Trigger.ToLowerInvariant()));
                    return Optional(matchedTrigger);
                },
                () => None,
                ex =>
                {
                    Log.Error(ex, "Failed to find mine triggers in chat {0}", chatId);
                    return None;
                });
    }

    public async Task<bool> DeleteMineTrigger(MineTrigger.CompositeId id, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var taskResult = session.IsNull()
            ? collection.DeleteOneAsync(m => m.Id == id, cancellationToken: cancelToken.Token)
            : collection.DeleteOneAsync(session, m => m.Id == id, cancellationToken: cancelToken.Token);
        return await taskResult
            .ToTryAsync().Match(result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete mine trigger with id: {0}", id);
                    return false;
                });
    }

    private IMongoCollection<MineTrigger> GetCollection() =>
        db.GetCollection<MineTrigger>(nameof(MineTrigger));
}

