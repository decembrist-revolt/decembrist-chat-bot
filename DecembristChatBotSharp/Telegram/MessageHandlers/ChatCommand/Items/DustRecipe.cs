using DecembristChatBotSharp.Entity;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand.Items;

public record QuantityRange(int Min, int Max, Random Random)
{
    public int GetRandomQuantity() => Random.Next(Min, Max + 1);
}

public record QuantityContainer(int? FixedQuantity, QuantityRange Range)
{
    public int GetQuantity() => FixedQuantity ?? Range.GetRandomQuantity();
}

public record ItemReward(
    MemberItemType Item,
    QuantityContainer Quantity)
{
    public int GetActualQuantity() => Quantity.GetQuantity();
}

public record PremiumBonus(
    double Chance,
    MemberItemType Item,
    QuantityRange Quantity);

public record DustRecipe(
    ItemReward Reward,
    PremiumBonus? PremiumBonus = null);