using System.Text;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

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
        var chatTitle = await botClient.GetChatTitle(chatId, cancelToken.Token)
            .ToAsync()
            .IfNone(chatId.ToString);
        return await botClient.SendChatAction(telegramId, ChatAction.Typing, cancellationToken: cancelToken.Token)
            .ToTryAsync().Match(
                async exx =>
                {
                    var items = await memberItemRepository.GetItems(chatId, telegramId);
                    var taskSendMessage = items.MatchAsync(
                        async inventory => await SendInventoryMessage(telegramId, chatTitle, inventory),
                        async () => await SendEmptyInventory(telegramId));

                    return await Array(taskSendMessage,
                        messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
                },
                async ex =>
                    await messageAssistance.SendInviteToDirect(chatId, telegramId,
                        "https://t.me/?start=inventory@chatId",
                        "Откройте для инвентаря"));
    }

    private Task<Unit> SendEmptyInventory(long chatId)
    {
        var message = string.Format(appConfig.ItemConfig.EmptyInventoryMessage);
        return botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent empty inventory message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to sent empty inventory message to chat {0}", chatId), cancelToken.Token);
    }

    private Task<Unit> SendInventoryMessage(long chatId, string chatTitle, Dictionary<MemberItemType, int> items)
    {
        var message = BuildInventory(chatTitle, items);
        return botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Success to send inventory message for chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send inventory message for chat {0}", chatId), cancelToken.Token);
    }

    private string BuildInventory(string chatTitle, Dictionary<MemberItemType, int> inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Format(appConfig.ItemConfig.SuccessInventoryMessage, chatTitle));
        foreach (var (item, count) in inventory)
        {
            builder.AppendLine($"{item}: {count}");
        }

        return builder.ToString();
    }
}