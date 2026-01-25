using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class MinionInvitationRepository(MongoDatabase db, CancellationTokenSource cancelToken) : IRepository
{
    private const string MasterIndexName = $"{nameof(MinionInvitation)}_MasterIndex_V1";
    private const string ExpireIndexName = $"{nameof(MinionInvitation)}_ExpireIndex_V1";

    private IMongoCollection<MinionInvitation> GetCollection() =>
        db.GetCollection<MinionInvitation>(nameof(MinionInvitation));

    public async Task<Unit> EnsureIndexes()
    {
        var collection = GetCollection();

        // Master index for fast lookup by master
        var masterIndexKeys = Builders<MinionInvitation>.IndexKeys.Ascending(x => x.MasterTelegramId);
        var masterIndexModel = new CreateIndexModel<MinionInvitation>(masterIndexKeys, new CreateIndexOptions
        {
            Name = MasterIndexName
        });

        await collection.Indexes.CreateOneAsync(masterIndexModel, cancellationToken: cancelToken.Token);
        Log.Information("Ensured index {0} for {1}", MasterIndexName, nameof(MinionInvitation));

        // TTL index for automatic expiration
        var expireIndexKeys = Builders<MinionInvitation>.IndexKeys.Ascending(x => x.ExpiresAt);
        var expireIndexModel = new CreateIndexModel<MinionInvitation>(expireIndexKeys, new CreateIndexOptions
        {
            Name = ExpireIndexName,
            ExpireAfter = TimeSpan.Zero // MongoDB will use the ExpiresAt field value
        });

        await collection.Indexes.CreateOneAsync(expireIndexModel, cancellationToken: cancelToken.Token);
        Log.Information("Ensured TTL index {0} for {1}", ExpireIndexName, nameof(MinionInvitation));

        return unit;
    }

    /// <summary>
    /// Creates a new minion invitation
    /// </summary>
    public async Task<bool> AddInvitation(MinionInvitation invitation)
    {
        var collection = GetCollection();
        return await collection
            .InsertOneAsync(invitation, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ =>
                {
                    Log.Information("Minion invitation created: {0} -> {1} in chat {2}",
                        invitation.MasterTelegramId, invitation.Id.TelegramId, invitation.Id.ChatId);
                    return true;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to add minion invitation: {0} -> {1} in chat {2}",
                        invitation.MasterTelegramId, invitation.Id.TelegramId, invitation.Id.ChatId);
                    return false;
                });
    }

    /// <summary>
    /// Gets pending invitation by minion ID
    /// </summary>
    public async Task<Option<MinionInvitation>> GetInvitation(CompositeId minionId)
    {
        var collection = GetCollection();
        return await collection
            .Find(inv => inv.Id == minionId)
            .FirstOrDefaultAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(
                Optional,
                ex =>
                {
                    Log.Error(ex, "Failed to get minion invitation for {0} in chat {1}",
                        minionId.TelegramId, minionId.ChatId);
                    return Option<MinionInvitation>.None;
                });
    }

    /// <summary>
    /// Gets pending invitation by master ID
    /// </summary>
    public async Task<Option<MinionInvitation>> GetInvitationByMaster(long masterTelegramId, long chatId)
    {
        var collection = GetCollection();
        return await collection
            .Find(inv => inv.MasterTelegramId == masterTelegramId && inv.Id.ChatId == chatId)
            .FirstOrDefaultAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(
                Optional,
                ex =>
                {
                    Log.Error(ex, "Failed to get minion invitation by master {0} in chat {1}",
                        masterTelegramId, chatId);
                    return Option<MinionInvitation>.None;
                });
    }

    /// <summary>
    /// Removes invitation when accepted or cancelled
    /// </summary>
    public async Task<bool> RemoveInvitation(CompositeId minionId)
    {
        var collection = GetCollection();
        return await collection
            .DeleteOneAsync(inv => inv.Id == minionId, cancelToken.Token)
            .ToTryAsync()
            .Match(
                result =>
                {
                    if (result.DeletedCount > 0)
                    {
                        Log.Information("Minion invitation removed for {0} in chat {1}",
                            minionId.TelegramId, minionId.ChatId);
                        return true;
                    }

                    return false;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to remove minion invitation for {0} in chat {1}",
                        minionId.TelegramId, minionId.ChatId);
                    return false;
                });
    }

    /// <summary>
    /// Removes invitation by master (when master cancels)
    /// </summary>
    public async Task<bool> RemoveInvitationByMaster(long masterTelegramId, long chatId)
    {
        var collection = GetCollection();
        return await collection
            .DeleteOneAsync(inv => inv.MasterTelegramId == masterTelegramId && inv.Id.ChatId == chatId,
                cancelToken.Token)
            .ToTryAsync()
            .Match(
                result =>
                {
                    if (result.DeletedCount > 0)
                    {
                        Log.Information("Minion invitation removed by master {0} in chat {1}",
                            masterTelegramId, chatId);
                        return true;
                    }

                    return false;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to remove minion invitation by master {0} in chat {1}",
                        masterTelegramId, chatId);
                    return false;
                });
    }

    /// <summary>
    /// Removes expired invitations (cleanup job)
    /// </summary>
    public async Task<long> RemoveExpiredInvitations()
    {
        var collection = GetCollection();
        return await collection
            .DeleteManyAsync(inv => inv.ExpiresAt != null && inv.ExpiresAt < DateTime.UtcNow, cancelToken.Token)
            .ToTryAsync()
            .Match(
                result =>
                {
                    if (result.DeletedCount > 0)
                    {
                        Log.Information("Removed {0} expired minion invitations", result.DeletedCount);
                    }

                    return result.DeletedCount;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to remove expired minion invitations");
                    return 0L;
                });
    }
}