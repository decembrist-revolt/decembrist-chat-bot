using System.Text;
using DecembristChatBotSharp;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;


[Singleton]
public class InventoryService(
    BotClient botClient,
    MemberItemRepository memberItemRepository,
    AppConfig appConfig,
    CancellationTokenSource cancelToken)
{
    public async Task<string> GetInventoryMessage(long telegramId, string text)
    {
        var message = "OK";
        var parts = text.Split('_');
        if (parts.Length < 2) return message;
        {
            if (long.TryParse(parts[1], out var chatId))
            {
                return await GetInventory(chatId, telegramId);
            }
            else
            {
                Log.Error("Failed to parse chatId for inventory command");
            }
        }

        return message;
    }

    private async Task<string> GetInventory(long chatId, long telegramId)
    {
        var items = await memberItemRepository.GetItems(chatId, telegramId);

        var chatTitle = await botClient.GetChatTitle(chatId, cancelToken.Token)
            .ToAsync()
            .IfNone(chatId.ToString);

        return items.Count > 0
            ? BuildInventory(items, chatTitle)
            : string.Format(appConfig.ItemConfig.EmptyInventoryMessage, chatTitle);
    }

    private string BuildInventory(Dictionary<MemberItemType, int> inventory, string chatTitle)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Format(appConfig.ItemConfig.SuccessInventoryMessage, chatTitle));
        var itemCount = 0;
        foreach (var (item, count) in inventory)
        {
            builder.Append(itemCount < inventory.Count-1 ? "├─" : "└─");
            builder.AppendLine($"**{item}:** {count}");
            itemCount++;
        }

        return builder.ToString();
    }
}