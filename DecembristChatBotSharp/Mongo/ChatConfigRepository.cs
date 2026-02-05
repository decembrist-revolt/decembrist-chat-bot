using System.Linq.Expressions;
using DecembristChatBotSharp.Entity.Configs;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

public class ChatConfigRepository(MongoDatabase db, CancellationTokenSource cancelToken) : IRepository
{
    public Task<List<long>> GetChatIds()
    {
        return GetCollection()
            .Find(_ => true)
            .Project(c => c.ChatId)
            .ToListAsync(cancelToken.Token)
            .ToTryAsync()
            .Map(x => x)
            .IfFail(x =>
            {
                Log.Error("Get chat ids failed: {0}", x.Message);
                return [];
            });
    }

    public Task<Option<ChatConfig>> GetChatConfig(long chatId)
    {
        var collection = GetCollection();

        return collection
            .Find(c => c.ChatId == chatId)
            .FirstAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(x =>
            {
                Log.Information("Successfully got config from chatId: {chatId}", chatId);
                return Some(x);
            }, ex =>
            {
                Log.Error("Failed to get config from chatId: {chatId}", chatId);
                return Option<ChatConfig>.None;
            });
    }

    public Task<Option<bool>> IsChatConfigExist(long chatId)
    {
        var collection = GetCollection();
        return collection
            .CountDocumentsAsync(c => c.ChatId == chatId, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(x =>
            {
                Log.Information("Successfully check config from chatId: {chatId}", chatId);
                return Some(x > 0);
            }, ex =>
            {
                Log.Error("Failed to check config exist from chatId: {chatId}", chatId);
                return Option<bool>.None;
            });
    }

    public Task<Option<T>> GetSpecificConfig<T>(long chatId, Expression<Func<ChatConfig, T>> selector) where T : IConfig
    {
        var collection = GetCollection();
        var configName = typeof(T).Name;

        var parameter = selector.Parameters[0];
        var body = Expression.Convert(selector.Body, typeof(object));
        var objectSelector = Expression.Lambda<Func<ChatConfig, object>>(body, parameter);

        var projection = Builders<ChatConfig>.Projection
            .Include(c => c.ChatId)
            .Include(objectSelector);

        return collection
            .Find(c => c.ChatId == chatId)
            .Project<ChatConfig>(projection)
            .FirstAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(chatConfig =>
            {
                var compiledSelector = selector.Compile();
                var config = compiledSelector(chatConfig);

                Log.Information("Successfully got specific config {configName} from chatId: {chatId}", configName,
                    chatId);
                return config;
            }, _ =>
            {
                Log.Error("Failed to get specific config {configName} from chatId: {chatId}", configName, chatId);
                return Option<T>.None;
            });
    }

    public Task<Option<ChatConfig>> GetSpecificConfigs(
        long chatId,
        params Expression<Func<ChatConfig, object>>[] selectors)
    {
        var collection = GetCollection();
        var projection = Builders<ChatConfig>.Projection.Include(c => c.ChatId);

        foreach (var selector in selectors)
        {
            projection = projection.Include(selector);
        }

        return collection
            .Find(c => c.ChatId == chatId)
            .Project<ChatConfig>(projection)
            .FirstAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(
                chatConfig => Some(chatConfig),
                _ => Option<ChatConfig>.None);
    }

    public async Task<bool> InsertChatConfig(ChatConfig config)
    {
        return await GetCollection()
            .InsertOneAsync(config, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(_ =>
            {
                Log.Information("Successfully inserted config for chatId: {chatId}", config.ChatId);
                return true;
            }, ex =>
            {
                Log.Error(ex, "Failed to insert config for chatId: {chatId}", config.ChatId);
                return false;
            });
    }

    public async Task<bool> DeleteChatConfig(long chatId)
    {
        return await GetCollection()
            .DeleteOneAsync(c => c.ChatId == chatId, cancelToken.Token)
            .ToTryAsync()
            .Match(result =>
            {
                if (result.DeletedCount > 0)
                {
                    Log.Information("Successfully deleted config for chatId: {chatId}", chatId);
                    return true;
                }

                Log.Warning("No config found to delete for chatId: {chatId}", chatId);
                return false;
            }, ex =>
            {
                Log.Error(ex, "Failed to delete config for chatId: {chatId}", chatId);
                return false;
            });
    }

    private IMongoCollection<ChatConfig> GetCollection() => db.GetCollection<ChatConfig>(nameof(ChatConfig));
}