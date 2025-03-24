﻿using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class MongoDatabase(AppConfig appConfig, Lazy<IList<IRepository>> repositories)
{
    private readonly MongoClient _client = new(appConfig.MongoConfig.ConnectionString);

    public IMongoDatabase GetDatabase() => _client.GetDatabase(appConfig.MongoConfig.DatabaseName);

    public Task<IClientSessionHandle> OpenSession() => _client.StartSessionAsync();

    public IMongoCollection<T> GetCollection<T>(string collectionName) =>
        GetDatabase().GetCollection<T>(collectionName);

    public async Task<Unit> CheckConnection()
    {
        var timeout = appConfig.MongoConfig.ConnectionCheckTimeoutSeconds;
        using var cancelToken = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));

        return await _client.ListDatabaseNamesAsync(cancelToken.Token)
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