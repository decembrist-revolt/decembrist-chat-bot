using MongoDB.Driver;

namespace DecembristChatBotSharp.Mongo;

public class MongoDatabase(AppConfig appConfig)
{
    private readonly MongoClient _client = new(appConfig.MongoConfig.ConnectionString);

    public IMongoDatabase GetDatabase() => _client.GetDatabase(appConfig.MongoConfig.DatabaseName);

    public IMongoCollection<T> GetCollection<T>(string collectionName) =>
        GetDatabase().GetCollection<T>(collectionName);
}