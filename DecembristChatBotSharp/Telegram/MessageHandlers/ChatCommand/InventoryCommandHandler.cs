using System.Text;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class InventoryCommandHandler(
    MemberItemRepository memberItemRepository,
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    AppConfig appConfig,
    BotClient botClient,
    CancellationTokenSource cancelToken
) : ICommandHandler
{
    public string Command => "/inventory";
    public string Description => "Show users items";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        var locked = await lockRepository.TryAcquire(chatId, Command);
        if (!locked) return await messageAssistance.CommandNotReady(chatId, messageId, Command);
        var username = await botClient.GetUsername(chatId, telegramId, cancelToken.Token)
            .ToAsync()
            .IfNone(telegramId.ToString);
        var items = await memberItemRepository.GetItems(chatId, telegramId);
        var taskSendMessage = items.MatchAsync(
            async inventory => await SendInventoryMessage(chatId, username, inventory),
            async () => await SendEmptyInventory(chatId, username));

        return await Array(taskSendMessage,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private Task<Unit> SendEmptyInventory(long chatId, string username)
    {
        var message = string.Format(appConfig.ItemConfig.EmptyInventoryMessage, username);
        return botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent empty inventory message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to sent empty inventory message to chat {0}", chatId), cancelToken.Token);
    }

    private Task<Unit> SendInventoryMessage(long chatId, string username, Dictionary<MemberItemType, int> items)
    {
        var message = BuildInventory(username, items);
        return botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Success to send inventory message for chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send inventory message for chat {0}", chatId), cancelToken.Token);
    }

    private static string BuildInventory(string username, Dictionary<MemberItemType, int> inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{username} inventory:");
        foreach (var (item, count) in inventory)
        {
            builder.AppendLine($"{item}: {count}");
        }

        return builder.ToString();
    }
}