using DecembristChatBotSharp.Entity;
using Lamar;
using LanguageExt.Common;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class PremiumMemberRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    private const string ExpirePremiumMemberIndex = $"{nameof(PremiumMember)}_ExpireIndex_V1";

    public async Task<Unit> EnsureIndexes()
    {
        var collection = GetCollection();
        var indexes = await (await collection.Indexes.ListAsync(cancelToken.Token)).ToListAsync(cancelToken.Token);
        if (indexes.Any(index => index["name"] == ExpirePremiumMemberIndex)) return unit;

        var expireAtIndex = Builders<PremiumMember>.IndexKeys.Ascending(x => x.ExpirationDate);
        var options = new CreateIndexOptions
        {
            ExpireAfter = TimeSpan.Zero,
            Name = ExpirePremiumMemberIndex
        };
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<PremiumMember>(expireAtIndex, options));
        return unit;
    }

    public async Task<Either<Error, Option<PremiumMember>>> GetById(CompositeId id, IMongoSession session)
    {
        var collection = GetCollection();
        return await collection
            .Find(session, member => member.Id == id)
            .SingleOrDefaultAsync(cancelToken.Token)
            .ToTryAsync()
            .Map(Optional)
            .ToEither()
            .MapLeft(ex =>
            {
                var (telegramId, chatId) = id;
                Log.Error(ex, "Failed to get premium {0} in chat {1}", telegramId, chatId);
                return ex;
            });
    }

    public async Task<bool> IsPremium(CompositeId id)
    {
        var collection = GetCollection();
        return await collection
            .Find(member => member.Id == id)
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                var (telegramId, chatId) = id;
                Log.Error(ex, "Failed to check premium {0} in {1}", telegramId, chatId);
                return false;
            });
    }

    public async Task<AddPremiumMemberResult> AddPremiumMember(PremiumMember member, IMongoSession session)
    {
        var collection = GetCollection();
        var filter = Builders<PremiumMember>.Filter.Eq(m => m.Id, member.Id);
        var update = Builders<PremiumMember>.Update
            .Set(m => m.ExpirationDate, member.ExpirationDate)
            .Set(m => m.Level, member.Level);
        var options = new UpdateOptions { IsUpsert = true };

        return await collection
            .UpdateOneAsync(session, filter, update, options, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result switch
                {
                    { IsAcknowledged: true, UpsertedId: null } => AddPremiumMemberResult.Update,
                    { IsAcknowledged: true, UpsertedId: not null } => AddPremiumMemberResult.Add,
                    _ => AddPremiumMemberResult.Error
                },
                ex =>
                {
                    var (telegramId, chatId) = member.Id;
                    Log.Error(ex, "Failed to add premium {0} to {1}", telegramId, chatId);
                    return AddPremiumMemberResult.Error;
                });
    }

    public async Task<bool> RemovePremiumMember(CompositeId id, IMongoSession session)
    {
        var collection = GetCollection();
        return await collection
            .DeleteOneAsync(session, member => member.Id == id, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.IsAcknowledged && result.DeletedCount > 0,
                ex =>
                {
                    var (telegramId, chatId) = id;
                    Log.Error(ex, "Failed to remove premium {0} from {1}", telegramId, chatId);
                    return false;
                });
    }

    public async Task<Arr<long>> GetChatIds()
    {
        var collection = GetCollection();
        return await collection
            .Distinct(
                member => member.Id.ChatId,
                member => member.ExpirationDate > DateTime.UtcNow)
            .ToListAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(chatIds => chatIds.ToArray(), ex =>
            {
                Log.Error(ex, "Failed to get chat ids for {0}", nameof(PremiumMember));
                return [];
            });
    }

    public async Task<Arr<long>> GetTelegramIds(long chatId)
    {
        var collection = GetCollection();
        return await collection
            .Distinct(
                member => member.Id.TelegramId,
                member => member.Id.ChatId == chatId && member.ExpirationDate > DateTime.UtcNow)
            .ToListAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(telegramIds => telegramIds.ToArray(), ex =>
            {
                Log.Error(ex, "Failed to get telegram ids for {0}", chatId);
                return [];
            });
    }

    private IMongoCollection<PremiumMember> GetCollection() => db.GetCollection<PremiumMember>(nameof(PremiumMember));
}

public enum AddPremiumMemberResult
{
    Add,
    Update,
    Error
}