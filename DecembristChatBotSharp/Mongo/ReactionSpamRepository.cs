using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Service;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class ReactionSpamRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    private const string ExpireReactionSpamMemberIndex = $"{nameof(ReactionSpamMember)}_ExpireIndex_V1";
    
    public async Task<Unit> EnsureIndexes()
    {
        var collection = GetCollection();
        var indexes = await (await collection.Indexes.ListAsync(cancelToken.Token)).ToListAsync(cancelToken.Token);
        if (indexes.Any(index => index["name"] == ExpireReactionSpamMemberIndex)) return unit;
        
        var expireAtIndex = Builders<ReactionSpamMember>.IndexKeys.Ascending(x => x.ExpireAt);
        var options = new CreateIndexOptions
        {
            ExpireAfter = TimeSpan.Zero,
            Name = ExpireReactionSpamMemberIndex
        };
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<ReactionSpamMember>(expireAtIndex, options));
        return unit;
    }

    public async Task<CurseResult> AddReactionSpamMember(
        ReactionSpamMember member, IMongoSession? session = null)
    {
        var collection = GetCollection();

        var tryFind = await collection
            .Find(session, mmber => mmber.Id == member.Id)
            .SingleOrDefaultAsync(cancelToken.Token)
            .ToTryOption();
        if (tryFind.IsSome()) return CurseResult.Duplicate;

        return await collection
            .InsertOneAsync(session, member, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ => CurseResult.Success,
                ex =>
                {
                    Log.Error(ex, "Failed to add reaction spam member {0}", member.Id);
                    return CurseResult.Failed;
                });
    }

    public async Task<Option<ReactionSpamMember>> GetReactionSpamMember(CompositeId id) => await GetCollection()
        .Find(m => m.Id == id)
        .SingleOrDefaultAsync(cancelToken.Token)
        .ToTryOption()
        .Match(Optional, () => None, ex =>
        {
            Log.Error(ex, "Failed to get user with telegramId {0} in reaction spam db", id);
            return None;
        });

    public async Task<bool> DeleteReactionSpamMember(CompositeId id, IMongoSession? session = null)
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

    private IMongoCollection<ReactionSpamMember> GetCollection() =>
        db.GetCollection<ReactionSpamMember>(nameof(ReactionSpamMember));
}