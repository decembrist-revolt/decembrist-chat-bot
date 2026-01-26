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
            .FirstAsync()
            .ToTryAsync()
            .Match(Some, ex =>
            {
                Log.Error("Failed to get config from chatId: {chatId}", chatId);
                return Option<ChatConfig>.None;
            });
    }

    private IMongoCollection<ChatConfig> GetCollection() => db.GetCollection<ChatConfig>(nameof(ChatConfig));
}