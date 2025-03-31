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
    public async Task<ReactionSpamResult> AddReactionSpamMember(ReactionSpamMember member,
        IClientSessionHandle? session = null)
    {
        var collection = GetCollection();

        var tryFind = await collection.Find(m => m.Id == member.Id)
            .SingleOrDefaultAsync(cancelToken.Token)
            .ToTryOption();

        if (tryFind.IsSome()) return ReactionSpamResult.Duplicate;
        return await collection
            .InsertOneAsync(session, member, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ => ReactionSpamResult.Success,
                ex =>
                {
                    Log.Error(ex, "Failed to add reaction spam member {0}", member.Id);
                    return ReactionSpamResult.Failed;
                });
    }

    public async Task<Option<ReactionSpamMember>> GetReactionSpamMember(CompositeId id) => await GetCollection()
        .Find(m => m.Id == id)
        .SingleOrDefaultAsync(cancelToken.Token)
        .ToTryAsync()
        .Match(member => member.IsDefault() ? None : Some(member),
            ex =>
            {
                Log.Error(ex, "Failed to get user with telegramId {0} in reaction spam db", id);
                return None;
            });

    public async Task<bool> DeleteReactionSpamMember(CompositeId id) =>
        await GetCollection().DeleteOneAsync(m => m.Id == id, cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete user with telegramId {0} in reaction spam db", id);
                    return false;
                });

    private IMongoCollection<ReactionSpamMember> GetCollection() =>
        db.GetCollection<ReactionSpamMember>(nameof(ReactionSpamMember));

    public async Task<Unit> EnsureIndexes()
    {
        var collection = GetCollection();
        var expireAtIndex = Builders<ReactionSpamMember>.IndexKeys.Ascending(x => x.ExpireAt);
        var options = new CreateIndexOptions { ExpireAfter = TimeSpan.Zero };
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<ReactionSpamMember>(expireAtIndex, options));
        return unit;
    }
}