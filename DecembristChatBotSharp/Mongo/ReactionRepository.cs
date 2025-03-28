using DecembristChatBotSharp.Entity;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

public class ReactionRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    public async Task<bool> AddReactionMember(
        ReactionMember member,
        IClientSessionHandle? session = null)
    {
        var collection = GetCollection();

        var update = Builders<ReactionMember>.Update
            .Set(x => x.Id, member.Id);
        var options = new UpdateOptions { IsUpsert = true };
        Log.Information("repo react start");

        var filter = Builders<ReactionMember>.Filter.Eq(x => x.Id, member.Id);
        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, options, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, options, cancelToken.Token);

        Log.Information("Repo react end");
        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && (result.UpsertedId != null || result.ModifiedCount == 1),
            ex =>
            {
                Log.Error(ex, "Failed to add restrict for {0}", member.Id);
                return false;
            });
    }

    public async Task<Option<ReactionMember>> GetReactionMember(ReactionMember.CompositeId id) => await GetCollection()
        .Find(m => m.Id == id)
        .SingleOrDefaultAsync(cancelToken.Token)
        .ToTryAsync()
        .Match(member => member.IsDefault() ? None : Some(member),
            ex =>
            {
                Log.Error(ex, "Failed to get user with telegramId {0} in restrict db", id);
                return None;
            });

    public async Task<bool> DeleteReactionMember(ReactionMember.CompositeId id) =>
        await GetCollection().DeleteOneAsync(m => m.Id == id, cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete user with telegramId {0} in restrict db", id);
                    return false;
                });

    private IMongoCollection<ReactionMember> GetCollection() =>
        db.GetCollection<ReactionMember>(nameof(ReactionMember));
}