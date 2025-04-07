using System.Linq.Expressions;
using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class DislikeRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    private record DislikeGroup(int Count, long DisId, long[] members);

    public Task<Unit> AddDislikeMember(long telegramId, long chatId, long dislikeTelegramId)
    {
        //todo заменить на  update
        var collection = GetCollection();
        var dislikeMember = new DislikeMember((telegramId, chatId), dislikeTelegramId);
        return TryAsync(async () =>
        {
            await collection.InsertOneAsync(dislikeMember, cancellationToken: cancelToken.Token);
            return unit;
        }).Match(identity, ex =>
        {
            Log.Error(ex, "Failed to add dislike from {0} to {1} in chat {2}", telegramId, dislikeTelegramId, chatId);
            return unit;
        });
    }


    public async Task<Arr<long>> GetTopOneDislikeMember(
        long chatId, IClientSessionHandle? session = null)
    {
        var collection = GetCollection();
        var pipeline = new EmptyPipelineDefinition<DislikeMember>()
            .Match(m => m.Id.ChatId == chatId)
            .Group(m => m.DislikeTelegramId,
                g => new DislikeGroup
                    (g.Count(), g.Key, g.Select(x => x.Id.TelegramId).ToArray()))
            .Sort(Builders<DislikeGroup>.Sort.Descending(count => count.Count)).Limit(1);

        var cursor = session.IsNull()
            ? collection.Aggregate(pipeline, cancellationToken: cancelToken.Token).FirstOrDefaultAsync()
            : collection.Aggregate(session, pipeline, cancellationToken: cancelToken.Token).FirstOrDefaultAsync();
        return await cursor.ToTryAsync().Match(identity => identity.members, ex => Arr<long>.Empty);
    }

    public async Task<long> RemoveAllInChat(long chatId, IClientSessionHandle? session = null)
    {
        var collection = GetCollection();
        Expression<Func<DislikeMember, bool>> filter = member => member.Id.ChatId == chatId;
        var deleteTask = session.IsNull()
            ? collection.DeleteManyAsync(filter, cancellationToken: cancelToken.Token)
            : collection.DeleteManyAsync(session, filter, cancellationToken: cancelToken.Token);

        return await deleteTask.ToTryAsync().Match(
            result => result.DeletedCount,
            ex =>
            {
                Log.Error(ex, "Failed to remove likes for chat {0}", chatId);
                return 0;
            });
    }

    private IMongoCollection<DislikeMember> GetCollection() => db.GetCollection<DislikeMember>(nameof(DislikeMember));
}