using System.Linq.Expressions;
using DecembristChatBotSharp.Entity;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

public class FilterRecordRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken)
    : IRepository
{
    public async Task<bool> AddFilterRecord(FilterRecord record, IMongoSession session) =>
        await GetCollection().InsertOneAsync(session, record, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(_ => true,
                ex =>
                {
                    Log.Error(ex, "Failed to add filter record {0} in repository", record.Id);
                    return false;
                });

    public async Task<bool> DeleteFilterRecord(FilterRecord.CompositeId id, IMongoSession session) =>
        await GetCollection()
            .DeleteOneAsync(session, m => m.Id == id, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete lor record: {0} in lor records db", id);
                    return false;
                });

    public Task<bool> IsFilterRecordExist(FilterRecord.CompositeId id, IMongoSession? session = null)
    {
        var collection = session != null
            ? GetCollection().AsQueryable(session)
            : GetCollection().AsQueryable();
        return collection.AnyAsync(record => record.Id == id, cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find filter record with id: {0}", id);
                return false;
            });
    }

    private IMongoCollection<FilterRecord> GetCollection() => db.GetCollection<FilterRecord>(nameof(FilterRecord));
}