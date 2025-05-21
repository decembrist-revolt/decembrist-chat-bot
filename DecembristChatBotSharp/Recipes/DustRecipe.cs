using DecembristChatBotSharp.Entity;

namespace DecembristChatBotSharp.Recipes;

public record QuantityRange(int Min, int Max);

public record DustReward(
    MemberItemType Item,
    QuantityRange Range);

public record PremiumReward(
    MemberItemType Item,
    double Chance,
    int Quantity);

public record DustRecipe(
    DustReward Reward,
    PremiumReward? PremiumReward = null);