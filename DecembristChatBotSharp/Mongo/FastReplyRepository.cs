using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Service;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class FastReplyRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    private const string ExpireFastReplyIndex = $"{nameof(FastReply)}_ExpireIndex_V1";

    public async Task<Unit> EnsureIndexes()
    {
        var collection = GetCollection();
        var indexes = await (await collection.Indexes.ListAsync(cancelToken.Token)).ToListAsync(cancelToken.Token);
        if (indexes.Any(index => index["name"] == ExpireFastReplyIndex))
        {
            await collection.Indexes.DropOneAsync(ExpireFastReplyIndex, cancelToken.Token);
        }

        return unit;
    }

    public async Task<Option<int>> GetMessagesCount(long chatId) =>
        await GetCollection()
            .CountDocumentsAsync(m => m.Id.ChatId == chatId)
            .ToTryAsync()
            .Match(x => x == 0 ? None : Some((int)x),
                ex =>
                {
                    Log.Error(ex, "Failed get keys count in fast reply db for chat {0}", chatId);
                    return None;
                });

    public async Task<Option<List<(string, DateTime)>>> GetFastReplyMessages(long chatId, int skip = 0)
    {
        if (skip < 0) return None;
        return await GetCollection()
            .Find(m => m.Id.ChatId == chatId && m.MessageType == FastReplyType.Text)
            .SortBy(m => m.Id.Message)
            .Skip(skip)
            .Limit(ListService.ListRowLimit)
            .Project(record => new { record.Id.Message, record.ExpireAt })
            .ToListAsync()
            .ToTryAsync()
            .Match(items => items.Count != 0 ? Some(items.Select(i => (i.Message, i.ExpireAt)).ToList()) : None,
                ex =>
                {
                    Log.Error(ex, "Failed to get fast reply messages for chat {0}", chatId);
                    return None;
                });
    }

    public async Task<Option<FastReply>> FindOne(long chatId, string message, FastReplyType type)
    {
        var collection = GetCollection();
        var messageText = type == FastReplyType.Text ? message.ToLowerInvariant() : message;

        return await collection
            .Find(reply => reply.Id.ChatId == chatId
                           && reply.Id.Message == messageText
                           && reply.MessageType == type)
            .FirstOrDefaultAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(Optional, ex =>
            {
                Log.Error(ex, "Failed to find fast reply for message {0} with type {1}", message, type);
                return None;
            });
    }

    public async Task<InsertResult> AddFastReply(FastReply fastReply, IMongoSession session)
    {
        var collection = GetCollection();

        var tryFind = await collection.Find(reply => reply.Id == fastReply.Id)
            .SingleOrDefaultAsync(cancelToken.Token)
            .ToTryOption();

        if (tryFind.IsSome()) return InsertResult.Duplicate;

        return await collection
            .InsertOneAsync(session, fastReply, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ => InsertResult.Success,
                ex =>
                {
                    Log.Error(ex, "Failed to add fast reply {0}", fastReply);
                    return InsertResult.Failed;
                });
    }

    public async Task<Arr<FastReply>> GetExpiredFastReplies(IMongoSession session)
    {
        var collection = GetCollection();

        return await collection.Find(session, reply => reply.ExpireAt < DateTime.UtcNow)
            .ToListAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(
                replies => replies.ToArr(),
                ex =>
                {
                    Log.Error(ex, "Failed to get expired fast replies");
                    return Arr<FastReply>.Empty;
                });
    }

    public async Task<bool> DeleteFastReplies(Arr<FastReply> fastReplies, IMongoSession session)
    {
        if (fastReplies.IsEmpty) return true;

        var collection = GetCollection();
        var ids = fastReplies.Map(reply => reply.Id).ToList();

        var taskResult = await collection.DeleteManyAsync(session,
            reply => ids.Contains(reply.Id), cancellationToken: cancelToken.Token);

        return taskResult.DeletedCount > 0;
    }

    public async Task<bool> DeleteFastReply(FastReply.CompositeId id, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var taskResult = session.IsNull()
            ? collection.DeleteOneAsync(m => m.Id == id, cancellationToken: cancelToken.Token)
            : collection.DeleteOneAsync(session, m => m.Id == id, cancellationToken: cancelToken.Token);
        return await taskResult
            .ToTryAsync().Match(result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete fast reply: {id} in fast reply collection", id);
                    return false;
                });
    }

    private IMongoCollection<FastReply> GetCollection() => db.GetCollection<FastReply>(nameof(FastReply));

    public enum InsertResult
    {
        Success,
        Duplicate,
        Failed
    }
}