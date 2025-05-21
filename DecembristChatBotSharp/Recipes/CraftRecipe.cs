using DecembristChatBotSharp.Entity;

namespace DecembristChatBotSharp.Recipes;

public record CraftRecipe(List<InputItem> Inputs, List<OutputItem> Outputs);

public record OutputItem(
    MemberItemType Item,
    double Chance,
    int Quantity = 1
);

public record InputItem(
    MemberItemType Item,
    int Quantity = 1
);