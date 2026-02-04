using DecembristChatBotSharp.Entity.Configs;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

public class ChatConfigRepository(MongoDatabase db, CancellationTokenSource cancelToken) : IRepository
{
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