using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand.Items;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class DustService(AppConfig appConfig)
{
    public string HandleDust(MemberItemType item, long chatId, long telegramId)
    {
        var items = appConfig.DustConfig.DustRecipes;
        Log.Information(string.Join(" ", items.Select(x => x.Key)));
        var recipe = items[item];
        Log.Information(recipe.PremiumBonus.Item + recipe.PremiumBonus.Quantity.ToString());
        Log.Information(recipe.Reward.Quantity.GetQuantity().ToString());
        return recipe.Reward.Item + recipe.Reward.GetActualQuantity().ToString();
    }
}