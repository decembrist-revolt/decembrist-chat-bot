using System.Linq.Expressions;
using DecembristChatBotSharp.Entity.Configs;
using MongoDB.Bson;
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

    private IMongoCollection<ChatConfig> GetCollection() => db.GetCollection<ChatConfig>(nameof(ChatConfig));
}