using System.Linq.Expressions;
using DecembristChatBotSharp.Entity;
using Lamar;
using LanguageExt.Common;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class UserProductRepository(MongoDatabase db, CancellationTokenSource cancelToken) : IRepository
{
    public async Task<Either<Error, bool>> ExistsById(string id, IMongoSession session)
    {
        var collection = GetCollection();
        Expression<Func<UserProduct, bool>> filter = userProduct => userProduct.Id == id;
        return await collection
            .Find(session, filter)
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .ToEither()
            .BiMap(identity, ex =>
            {
                Log.Error(ex, "Failed to check existence of UserProduct with ID {0}", id);
                return ex;
            });
    }

    public Task<Unit> AddUserProduct(UserProduct userProduct, IMongoSession session)
    {
        var newMembers = GetCollection();
        return newMembers.InsertOneAsync(session, userProduct)
            .ToTryAsync()
            .IfFail(ex => Log.Error(ex, "Failed to add UserProduct: {0}", userProduct));
    }
    
    private IMongoCollection<UserProduct> GetCollection() => db.GetCollection<UserProduct>(nameof(UserProduct));
}