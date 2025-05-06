using DecembristChatBotSharp.Entity;
using Lamar;
using LanguageExt.Common;
using MongoDB.Driver;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class PollPaymentOffsetRepository(MongoDatabase db, CancellationTokenSource cancelToken)
{
    public async Task<Either<Error, Option<PollPaymentOffset>>> Get(IMongoSession session) => await GetCollection()
        .Find(session, Builders<PollPaymentOffset>.Filter.Empty)
        .FirstOrDefaultAsync(cancelToken.Token)
        .ToTryAsync()
        .Map(Optional)
        .ToEither();

    public async Task<Either<Error, PollPaymentOffset>> Set(long offset, IMongoSession session)
    {
        var collection = GetCollection();
        var filter = Builders<PollPaymentOffset>.Filter.Empty;
        var update = Builders<PollPaymentOffset>.Update
            .Set(pp => pp.Offset, offset)
            .Set(pp => pp.LastUpdatedAt, DateTime.UtcNow);
        var options = new UpdateOptions { IsUpsert = true };

        return await collection.UpdateOneAsync(session, filter, update, options, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Ensure(result => result.IsAcknowledged, "Set page failed")
            .Map(_ => new PollPaymentOffset(offset, DateTime.UtcNow))
            .ToEither();
    }

    private IMongoCollection<PollPaymentOffset> GetCollection() =>
        db.GetCollection<PollPaymentOffset>(nameof(PollPaymentOffset));
}