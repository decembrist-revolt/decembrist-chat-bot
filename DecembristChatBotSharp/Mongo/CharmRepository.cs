using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Service;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class CharmRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    private const string ExpireCharmMemberIndex = $"{nameof(CharmMember)}_ExpireIndex_V1";

    public async Task<Unit> EnsureIndexes()
    {
        var collection = GetCollection();
        var indexes = await (await collection.Indexes.ListAsync(cancelToken.Token)).ToListAsync(cancelToken.Token);
        if (indexes.Any(index => index["name"] == ExpireCharmMemberIndex)) return unit;

        var expireAtIndex = Builders<CharmMember>.IndexKeys.Ascending(x => x.ExpireAt);
        var options = new CreateIndexOptions
        {
            ExpireAfter = TimeSpan.Zero,
            Name = ExpireCharmMemberIndex
        };
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<CharmMember>(expireAtIndex, options));
        return unit;
    }

    public async Task<CharmResult> AddCharmMember(
        CharmMember member, IMongoSession? session = null)
    {
        var collection = GetCollection();

        var tryFind = await collection
            .Find(session, mmber => mmber.Id == member.Id)
            .SingleOrDefaultAsync(cancelToken.Token)
            .ToTryOption();
        if (tryFind.IsSome()) return CharmResult.Duplicate;

        return await collection
            .InsertOneAsync(session, member, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ => CharmResult.Success,
                ex =>
                {
                    Log.Error(ex, "Failed to add charm to repository {0}", member.Id);
                    return CharmResult.Failed;
                });
    }

    public async Task<Option<CharmMember>> GetCharmMember(CompositeId id) => await GetCollection()
        .Find(m => m.Id == id)
        .SingleOrDefaultAsync(cancelToken.Token)
        .ToTryAsync()
        .Match(member => member.IsDefault() ? None : Some(member),
            ex =>
            {
                Log.Error(ex, "Failed to get user with telegramId {0} in charm db", id);
                return None;
            });

    public async Task<bool> DeleteCharmMember(CompositeId id, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var taskResult = session.IsNull()
            ? collection.DeleteOneAsync(m => m.Id == id, cancellationToken: cancelToken.Token)
            : collection.DeleteOneAsync(session, m => m.Id == id, cancellationToken: cancelToken.Token);
        return await taskResult
            .ToTryAsync().Match(result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete charm to user with telegramId {0} in charm collection", id);
                    return false;
                });
    }

    private IMongoCollection<CharmMember> GetCollection() =>
        db.GetCollection<CharmMember>(nameof(CharmMember));
}