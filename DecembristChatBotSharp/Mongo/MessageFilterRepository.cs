using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class MessageFilterRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken)
    : IRepository
{
    public async Task<bool> AddFilteredMessage(FilteredMessage message, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var query = session != null
            ? collection.InsertOneAsync(session, message, cancellationToken: cancelToken.Token)
            : collection.InsertOneAsync(message, cancellationToken: cancelToken.Token);
        return await query.ToTryAsync()
            .Match(_ => true,
                ex =>
                {
                    Log.Error(ex, "Failed to add filter message {0} in filter db", message.Id);
                    return false;
                });
    }

    public async Task<Option<FilteredMessage>> GetFilteredMessage(long chatId, long telegramId) =>
        await GetCollection()
            .Find(m => m.Id.ChatId == chatId && m.OwnerId == telegramId)
            .SingleOrDefaultAsync(cancelToken.Token)
            .ToTryOption()
            .Match(Optional, () => None, ex =>
            {
                Log.Error(ex, "Failed to get filter message: owner: {0}, chat:{1} in filter db", telegramId,
                    chatId);
                return None;
            });

    public async Task<bool> RemoveFilteredMessage(FilteredMessage.CompositeId id, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var taskResult = session.IsNull()
            ? collection.DeleteOneAsync(m => m.Id == id, cancellationToken: cancelToken.Token)
            : collection.DeleteOneAsync(session, m => m.Id == id, cancellationToken: cancelToken.Token);
        return await taskResult
            .ToTryAsync().Match(result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete filter message: {0} in filter db", id);
                    return false;
                });
    }

    public Task<List<FilteredMessage>> GetExpiredMessages(DateTime olderThan) =>
        GetCollection().Find(member => member.CreatedAt < olderThan)
            .ToListAsync(cancelToken.Token).ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to get filter messages from repository");
                return [];
            });

    private IMongoCollection<FilteredMessage> GetCollection() =>
        db.GetCollection<FilteredMessage>(nameof(FilteredMessage));
}