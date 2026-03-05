using System.Linq.Expressions;
using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class FilteredMessageRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken)
    : IRepository
{
    public async Task<bool> AddFilteredMessage(FilteredMessage message, IMongoSession? session = null)
    {
        var collection = GetCollection();

        var update = Builders<FilteredMessage>.Update
            .Set(m => m.Id, message.Id)
            .Set(m => m.MessageId, message.MessageId)
            .Set(m => m.CaptchaMessageId, message.CaptchaMessageId)
            .Set(m => m.CreatedAt, message.CreatedAt)
            .Inc(m => m.TryCount, 1);

        var options = new UpdateOptions { IsUpsert = true };
        Expression<Func<FilteredMessage, bool>> findExpr = m => m.Id == message.Id;

        var updateTask = !session.IsNull()
            ? collection.UpdateOneAsync(session, findExpr, update, options, cancelToken.Token)
            : collection.UpdateOneAsync(findExpr, update, options, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && (result.UpsertedId != null || result.ModifiedCount == 1),
            ex =>
            {
                Log.Error(ex, "Failed to add filtered message {0} in repository", message.Id);
                return false;
            });
    }

    public async Task<Option<FilteredMessage>> GetFilteredMessage(CompositeId id) =>
        await GetCollection()
            .Find(m => m.Id == id)
            .SingleOrDefaultAsync(cancelToken.Token)
            .ToTryOption()
            .Match(Optional, () => None, ex =>
            {
                Log.Error(ex, "Failed to get filtered message: owner: {0}, chat:{1} in filtered db", id.TelegramId,
                    id.ChatId);
                return None;
            });

    public async Task<bool> DeleteFilteredMessage(CompositeId id, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var taskResult = session.IsNull()
            ? collection.DeleteOneAsync(m => m.Id == id, cancellationToken: cancelToken.Token)
            : collection.DeleteOneAsync(session, m => m.Id == id, cancellationToken: cancelToken.Token);
        return await taskResult
            .ToTryAsync().Match(result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete filtered message with id {0} in filtered db", id);
                    return false;
                });
    }

    public Task<List<FilteredMessage>> GetExpiredMessages(DateTime olderThan) =>
        GetCollection().Find(member => member.CreatedAt < olderThan)
            .ToListAsync(cancelToken.Token).ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to get filtered messages from filter repository");
                return [];
            });

    private IMongoCollection<FilteredMessage> GetCollection() =>
        db.GetCollection<FilteredMessage>(nameof(FilteredMessage));
}