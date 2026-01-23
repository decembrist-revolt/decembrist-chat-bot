using DecembristChatBotSharp.Entity;
using MongoDB.Driver;

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
            .Match(Some, _ => Option<ChatConfig>.None);
    }

    private IMongoCollection<ChatConfig> GetCollection() => db.GetCollection<ChatConfig>(nameof(ChatConfig));
}