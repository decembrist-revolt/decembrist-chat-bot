using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class RestrictRepository(
    MongoDatabase db,
    AdminUserRepository adminRepository,
    CancellationTokenSource cancelToken) : IRepository
{
    public async Task<bool> IsAdmin(long id) => await adminRepository.IsAdmin(id);

    public async Task<Unit> AddRestrict(RestrictMember member) =>
        await IsRestricted(member.Id)
            .ToTryAsync()
            .Bind(exists => exists
                ? UpdateRestrict(member)
                : TryInsertRestrict(member))
            .Match(
                _ => unit,
                ex =>
                {
                    Log.Error(ex, "Failed to add restrict for {MemberId}", member.Id);
                    return unit;
                });

    private TryAsync<Unit> UpdateRestrict(RestrictMember member)
    {
        var collection = GetCollection();
        var filter = Builders<RestrictMember>.Filter.Eq(x => x.Id, member.Id);
        var update = Builders<RestrictMember>.Update
            .Set(x => x.RestrictType, member.RestrictType);

        return TryAsync(async () =>
        {
            await collection.UpdateOneAsync(filter, update, cancellationToken: cancelToken.Token);
            return unit;
        });
    }

    private TryAsync<Unit> TryInsertRestrict(RestrictMember member)
    {
        var collection = GetCollection();
        return TryAsync(async () =>
        {
            await collection.InsertOneAsync(member, cancellationToken: cancelToken.Token);
            return unit;
        });
    }

    public async Task<RestrictMember> GetMember(RestrictMember member) => await GetCollection()
        .Find(m => m.Id == member.Id)
        .SingleOrDefaultAsync(cancelToken.Token);

    public async Task<bool> IsRestricted(RestrictMember.CompositeId Id)
    {
        var collection = GetCollection();

        return await collection
            .Find(m => m.Id == Id)
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find user with telegramId {0} in restrict db", Id);
                return false;
            });
    }

    public async Task<Unit> DeleteMember(RestrictMember.CompositeId id)
    {
        var collection = GetCollection();
        var res = await IsRestricted(id);
        if (!res)
            return unit;
        return await collection.DeleteOneAsync(m => m.Id == id)
            .ToTryAsync()
            .Match(identity => unit,
                ex =>
                {
                    Log.Error(ex, "Failed to delete user with telegramId {0} in restrict db", id);
                    return unit;
                });
    }

    private IMongoCollection<RestrictMember> GetCollection() =>
        db.GetCollection<RestrictMember>(nameof(RestrictMember));
}