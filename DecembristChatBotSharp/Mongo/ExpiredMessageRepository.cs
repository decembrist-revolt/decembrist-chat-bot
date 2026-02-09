using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Service;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class ExpiredMessageRepository(
    AppConfig appConfig,
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    public async Task<Unit> QueueMessage(long chatId, int messageId, DateTime? expirationDate = null)
    {
        var collection = GetCollection();
        var date = expirationDate ??
                   DateTime.UtcNow.AddSeconds(appConfig.CommandAssistanceConfig.CommandIntervalSeconds);
        var message = new ExpiredMessage(new ExpiredMessage.CompositeId(chatId, messageId), date);
        return await collection.InsertOneAsync(message, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .IfFail(ex => Log.Error(ex, "Failed to queue message {0} for chat {1}", messageId, chatId));
    }

    public async Task<Arr<ExpiredMessage>> GetExpiredMessages()
    {
        var collection = GetCollection();

        return await collection.Find(message => message.ExpirationDate < DateTime.UtcNow)
            .ToListAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(messages => messages.ToArr(), ex =>
            {
                Log.Error(ex, "Failed to get expired messages");
                return [];
            });
    }

    public async Task<Unit> DeleteMessages(Arr<ExpiredMessage> messages)
    {
        var collection = GetCollection();

        var messageIds = messages.Map(message => message.Id).ToArray();
        return await collection.DeleteManyAsync(
                message => messageIds.Contains(message.Id), cancelToken.Token)
            .ToTryAsync()
            .IfFail(ex => Log.Error(ex, "Failed to delete expired messages"));
    }

    private IMongoCollection<ExpiredMessage> GetCollection() =>
        db.GetCollection<ExpiredMessage>(nameof(ExpiredMessage));
}