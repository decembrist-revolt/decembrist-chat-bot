using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram;

[Singleton]
public class ExpiredMessageService(
    AppConfig appConfig,
    ExpiredMessageRepository repository, 
    BotClient botClient,
    CancellationTokenSource cancelToken)
{
    private Timer? _timer;

    public Unit Start()
    {
        var intervalSeconds = appConfig.CommandConfig.CommandIntervalSeconds;
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        _timer = new Timer(
            _ => CheckMessages().Wait(cancelToken.Token), null, interval, interval);

        cancelToken.Token.Register(_ => _timer.Dispose(), null);

        return unit;
    }

    private async Task<Unit> CheckMessages()
    {
        var messages = await repository.GetExpiredMessages();
        var chatIdToMessageIds = messages
            .GroupBy(message => message.Id.ChatId, message => message.Id.MessageId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        await chatIdToMessageIds.Map(group => DeleteMessages(group.Key, group.Value)).WhenAll();
        await repository.DeleteMessages(messages);

        return unit;
    }
    
    private async Task<Unit> DeleteMessages(long chatId, int[] messageIds) =>
        await botClient.DeleteMessages(chatId, messageIds, cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ => Log.Information("Deleted expired message {0} in chat {1}", messageIds, chatId),
                ex => Log.Error(ex, "Failed to delete expired message {0} in chat {1}", messageIds, chatId)
            );
}