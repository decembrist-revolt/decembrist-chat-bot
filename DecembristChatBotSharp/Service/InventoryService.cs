using System.Text;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class InventoryService(
    BotClient botClient,
    MemberItemRepository memberItemRepository,
    AppConfig appConfig,
    CancellationTokenSource cancelToken)
{
    public async Task<string> GetInventory(long chatId, long telegramId)
    {
        var items = await memberItemRepository.GetItems(chatId, telegramId);

        var chatTitle = await botClient.GetChatTitle(chatId, cancelToken.Token)
            .ToAsync()
            .IfNone(chatId.ToString);

        return items.Count > 0
            ? BuildInventory(items, chatTitle)
            : string.Format(appConfig.ItemConfig.EmptyInventoryMessage, chatTitle);
    }

    private string BuildInventory(Map<MemberItemType, int> inventory, string chatTitle)
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