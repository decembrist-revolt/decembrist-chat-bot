using DecembristChatBotSharp.Entity;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand.Items;

public record CraftRecipe(List<InputItem> Inputs, List<OutputItem> Outputs)
{
    public static int CalculateRecipeHash(List<InputItem> inputs)
    {
        var hash = new HashCode();
        var orderByInputs = inputs
            .GroupBy(x => x.Item)
            .Select(g => new { Item = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .OrderBy(x => x.Item);

        foreach (var item in orderByInputs)
        {
            hash.Add(item.Item);
            hash.Add(item.Quantity);
        }

        return hash.ToHashCode();
    }
};

public record OutputItem(
    MemberItemType Item,
    double Chance,
    int Quantity = 1
);

public record InputItem(
    MemberItemType Item,
    int Quantity = 1
);