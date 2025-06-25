using System.Linq.Expressions;
using DecembristChatBotSharp.Entity;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

public class FilterRecordRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken)
    : IRepository
{
    public async Task<bool> AddFilterRecord(FilterRecord record, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var query = session != null
            ? collection.InsertOneAsync(session, record, cancellationToken: cancelToken.Token)
            : collection.InsertOneAsync(record, cancellationToken: cancelToken.Token);
        return await query.ToTryAsync()
            .Match(_ => true,
                ex =>
                {
                    Log.Error(ex, "Failed to add filter record {0} in repository", record.Id);
                    return false;
                });
    }

    public Task<bool> IsFilterRecordExist(FilterRecord.CompositeId id, IMongoSession? session = null)
    {
        var collection = GetCollection();

        Expression<Func<FilterRecord, bool>> filter = phrase => phrase.Id == id;
        var findTask = session.IsNull()
            ? collection.Find(filter)
            : collection.Find(session, filter);

        return findTask
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find filter record with id: {0}", id);
                return false;
            });
    }

    private IMongoCollection<FilterRecord> GetCollection() => db.GetCollection<FilterRecord>(nameof(FilterRecord));
}