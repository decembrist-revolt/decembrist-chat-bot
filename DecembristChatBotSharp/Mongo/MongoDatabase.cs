using Lamar;
using MongoDB.Driver;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class MongoDatabase(AppConfig appConfig, Lazy<IList<IRepository>> repositories)
{
    private readonly MongoClient _client = new(appConfig.MongoConfig.ConnectionString);

    public IMongoDatabase GetDatabase() => _client.GetDatabase(appConfig.MongoConfig.DatabaseName);

    public Task<IClientSessionHandle> OpenTransaction() => _client.StartSessionAsync();

    public IMongoCollection<T> GetCollection<T>(string collectionName) =>
        GetDatabase().GetCollection<T>(collectionName);

    public async Task EnsureIndexes() =>
        await repositories.Value.Map(repository => repository.EnsureIndexes()).WhenAll();
}