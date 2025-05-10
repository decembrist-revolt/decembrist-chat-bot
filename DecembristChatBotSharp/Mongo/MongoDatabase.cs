global using IMongoSession = MongoDB.Driver.IClientSessionHandle;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class MongoDatabase(
    AppConfig appConfig, 
    MongoClient client,
    MongoUrl mongoUrl,
    Lazy<IList<IRepository>> repositories)
{
    public IMongoDatabase GetDatabase() => client.GetDatabase(mongoUrl.DatabaseName);

    public Task<IMongoSession> OpenSession() => client.StartSessionAsync();

    public IMongoCollection<T> GetCollection<T>(string collectionName) =>
        GetDatabase().GetCollection<T>(collectionName);

    public async Task<Unit> CheckConnection()
    {
        var timeout = appConfig.MongoConfig.ConnectionCheckTimeoutSeconds;
        using var cancelToken = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));

        return await client.ListDatabaseNamesAsync(cancelToken.Token)
            .ToTryAsync()
            .IfFail(static void (ex) =>
            {
                Log.Error(ex, "Timeout check connection to MongoDB");
                throw ex;
            });
    }

    public async Task EnsureIndexes() =>
        await repositories.Value.Map(repository => repository.EnsureIndexes()).WhenAll();
}