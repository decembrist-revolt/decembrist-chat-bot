using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class GiveawayParticipantRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    private const string ExpireGiveawayParticipantIndex = $"{nameof(GiveawayParticipant)}_ExpireIndex_V1";

    public async Task<Unit> EnsureIndexes()
    {
        var collection = GetCollection();
        var indexes = await (await collection.Indexes.ListAsync(cancelToken.Token)).ToListAsync(cancelToken.Token);
        if (indexes.Any(index => index["name"] == ExpireGiveawayParticipantIndex)) return unit;

        var expireAtIndex = Builders<GiveawayParticipant>.IndexKeys.Ascending(x => x.ExpireAt);
        var options = new CreateIndexOptions
        {
            ExpireAfter = TimeSpan.Zero,
            Name = ExpireGiveawayParticipantIndex
        };
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<GiveawayParticipant>(expireAtIndex, options));
        return unit;
    }

    public async Task<bool> AddParticipant(GiveawayParticipant participant, IMongoSession? session = null)
    {
        var collection = GetCollection();

        var filter = Builders<GiveawayParticipant>.Filter.Eq(x => x.Id, participant.Id);
        var options = new UpdateOptions { IsUpsert = true };
        
        var update = Builders<GiveawayParticipant>.Update
            .Set(x => x.ReceivedAt, participant.ReceivedAt)
            .Set(x => x.ExpireAt, participant.ExpireAt);

        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, options, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, options, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && (result.UpsertedId != null || result.ModifiedCount > 0),
            ex =>
            {
                Log.Error(ex, "Failed to add giveaway participant id: {0}", participant.Id);
                return false;
            });
    }

    public Task<bool> HasParticipated(GiveawayParticipant.CompositeId id)
    {
        var collection = GetCollection();

        return collection
            .Find(participant => participant.Id == id)
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find participant with id: {0}", id);
                return false;
            });
    }

    private IMongoCollection<GiveawayParticipant> GetCollection() =>
        db.GetCollection<GiveawayParticipant>(nameof(GiveawayParticipant));
}

