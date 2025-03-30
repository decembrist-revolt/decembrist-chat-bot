using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Service;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

public class ReactionRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    public async Task<UseFastReplyResult> AddReactionMember(ReactionMember member,
        IClientSessionHandle? session = null)
    {
        Log.Information("start re rpo");
        var collection = GetCollection();

        var update = Builders<ReactionMember>.Update
            .Set(x => x.Emoji, member.Emoji)
            .Set(x => x.Date, DateTime.UtcNow);
        var options = new UpdateOptions { IsUpsert = true };

        var filter = Builders<ReactionMember>.Filter.Eq(x => x.Id, member.Id);
        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, options, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, options, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && (result.UpsertedId != null || result.ModifiedCount == 1)
                ? UseFastReplyResult.Success
                : UseFastReplyResult.Failed,
            ex =>
            {
                Log.Error(ex, "Failed to add restrict for {0}", member.Id);
                return UseFastReplyResult.Failed;
            });
    }

    public async Task<Option<ReactionMember>> GetReactionMember(CompositeId id) => await GetCollection()
        .Find(m => m.Id == id)
        .SingleOrDefaultAsync(cancelToken.Token)
        .ToTryAsync()
        .Match(member => member.IsDefault() ? None : Some(member),
            ex =>
            {
                Log.Error(ex, "Failed to get user with telegramId {0} in restrict db", id);
                return None;
            });

    public async Task<bool> DeleteReactionMember(CompositeId id) =>
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