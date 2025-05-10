using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class RestrictRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    public async Task<bool> AddRestrict(
        RestrictMember member,
        IMongoSession? session = null)
    {
        var collection = GetCollection();

        var update = Builders<RestrictMember>.Update
            .Set(x => x.RestrictType, member.RestrictType);
        var options = new UpdateOptions { IsUpsert = true };

        var filter = Builders<RestrictMember>.Filter.Eq(x => x.Id, member.Id);
        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, options, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, options, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && (result.UpsertedId != null || result.ModifiedCount == 1),
            ex =>
            {
                Log.Error(ex, "Failed to add restrict for {0}", member.Id);
                return false;
            });
    }

    public async Task<Option<RestrictMember>> GetRestrictMember(CompositeId id) => await GetCollection()
        .Find(m => m.Id == id)
        .SingleOrDefaultAsync(cancelToken.Token)
        .ToTryAsync()
        .Match(member => member.IsDefault() ? None : Some(member),
            ex =>
            {
                Log.Error(ex, "Failed to get user with telegramId {0} in restrict db", id);
                return None;
            });

    public async Task<bool> DeleteRestrictMember(CompositeId id) =>
        await GetCollection().DeleteOneAsync(m => m.Id == id, cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete user with telegramId {0} in restrict db", id);
                    return false;
                });

    private IMongoCollection<RestrictMember> GetCollection() =>
        db.GetCollection<RestrictMember>(nameof(RestrictMember));
}