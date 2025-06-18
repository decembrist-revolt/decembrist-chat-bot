using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class SuspiciousMessageRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken)
    : IRepository
{
    public async Task<bool> AddSuspiciousMessage(SuspiciousMessage message, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var query = session != null
            ? collection.InsertOneAsync(session, message, cancellationToken: cancelToken.Token)
            : collection.InsertOneAsync(message, cancellationToken: cancelToken.Token);
        return await query.ToTryAsync()
            .Match(_ => true,
                ex =>
                {
                    Log.Error(ex, "Failed to add suspicious message {0} in repository", message.Id);
                    return false;
                });
    }

    public async Task<Option<SuspiciousMessage>> GetCurseMember(long chatId, long telegramId) =>
        await GetCollection()
            .Find(m => m.Id.ChatId == chatId && m.OwnerId == telegramId)
            .SingleOrDefaultAsync(cancelToken.Token)
            .ToTryOption()
            .Match(Optional, () => None, ex =>
            {
                Log.Error(ex, "Failed to get suspicious message: owner: {0}, chat:{1} in suspicious db", telegramId,
                    chatId);
                return None;
            });

    public async Task<bool> DeleteSuspiciousMessage(SuspiciousMessage.CompositeId id, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var taskResult = session.IsNull()
            ? collection.DeleteOneAsync(m => m.Id == id, cancellationToken: cancelToken.Token)
            : collection.DeleteOneAsync(session, m => m.Id == id, cancellationToken: cancelToken.Token);
        return await taskResult
            .ToTryAsync().Match(result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete user with telegramId {0} in reaction spam db", id);
                    return false;
                });
    }

    public Task<List<SuspiciousMessage>> GetExpiredMessages(DateTime olderThan) =>
        GetCollection().Find(member => member.CreatedAt < olderThan)
            .ToListAsync(cancelToken.Token).ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to get chat ids from dislikes repository");
                return [];
            });

    private IMongoCollection<SuspiciousMessage> GetCollection() =>
        db.GetCollection<SuspiciousMessage>(nameof(SuspiciousMessage));
}