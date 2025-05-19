using DecembristChatBotSharp.Entity;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand.Items;

public record QuantityRange(int Min, int Max);

public record ItemReward(
    MemberItemType Item,
    QuantityRange Range);

public record PremiumBonus(
    MemberItemType Item,
    double Chance,
    int Quantity);

public record DustRecipe(
    ItemReward Reward,
    PremiumBonus? PremiumBonus = null);