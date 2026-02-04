using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class CraftService(
    Random random,
    AppConfig appConfig,
    MongoDatabase db,
    HistoryLogRepository historyLogRepository,
    MemberItemRepository memberItemRepository,
    CancellationTokenSource cancelToken,
    PremiumMemberRepository premiumMemberRepository)
{
    private readonly Dictionary<int, CraftRecipe> _recipeCache =
        appConfig.CraftRecipesConfig.Recipes.ToDictionary(recipe => CalculateRecipeHash(recipe.Inputs));

    public async Task<CraftOperationResult> HandleCraft(List<ItemQuantity> input, long chatId, long telegramId)
    {
        var inputHash = CalculateRecipeHash(input);
        if (!_recipeCache.TryGetValue(inputHash, out var recipe)) return new CraftOperationResult(CraftResult.NoRecipe);

        using var session = await db.OpenSession();
        session.StartTransaction();

        var hasItem = await memberItemRepository.RemoveMemberItems(chatId, telegramId, recipe.Inputs, session);

        return hasItem
            ? await ProcessCraftOperation(recipe, chatId, telegramId, session)
            : await AbortWithResult(session, CraftResult.NoItems);
    }

    private async Task<CraftOperationResult> ProcessCraftOperation(
        CraftRecipe recipe, long chatId, long telegramId, IMongoSession session)
    {
        var craftItem = GetRandomOutputItem(recipe.Outputs);

        var isGetPremiumBonus = await premiumMemberRepository.IsPremium((telegramId, chatId)) && IsGetPremiumBonus();
        if (isGetPremiumBonus)
        {
            craftItem = craftItem with { Quantity = craftItem.Quantity + 1 };
        }

        var result = isGetPremiumBonus ? CraftResult.PremiumSuccess : CraftResult.Success;

        var isAdd = await AddCraftItems(chatId, telegramId, craftItem, session);

        await LogInHistory(recipe.Inputs, chatId, telegramId, craftItem, session);

        return isAdd
            ? await CommitWithResult(session, new CraftOperationResult(result, craftItem))
            : await AbortWithResult(session);
    }

    private async Task LogInHistory(
        List<ItemQuantity> removeItems, long chatId, long telegramId, ItemQuantity itemQuantity, IMongoSession session)
    {
        var itemsLog = removeItems
            .Select(x => x with { Quantity = -x.Quantity })
            .Append(new ItemQuantity(itemQuantity.Item, itemQuantity.Quantity))
            .ToList();
        await historyLogRepository.LogDifferentItems(chatId, telegramId, itemsLog, session, MemberItemSourceType.Craft);
    }

    private async Task<bool> AddCraftItems(
        long chatId, long telegramId, ItemQuantity itemQuantity, IMongoSession session) =>
        await memberItemRepository.AddMemberItem(chatId, telegramId, itemQuantity.Item, session, itemQuantity.Quantity);

    private ItemQuantity GetRandomOutputItem(List<OutputItem> outputs)
    {
        if (outputs.Count == 1)
        {
            var first = outputs.First();
            return new ItemQuantity(first.Item, first.Quantity);
        }

        var total = outputs.Sum(x => x.Chance);
        var roll = random.NextDouble() * total;

        var cumulative = 0.0;

        foreach (var output in outputs)
        {
            cumulative += output.Chance;
            if (roll <= cumulative) return new ItemQuantity(output.Item, output.Quantity);
        }

        var last = outputs.Last();

        return new ItemQuantity(last.Item, last.Quantity);
    }

    private bool IsGetPremiumBonus() => random.NextDouble() < appConfig.CraftRecipesConfig.PremiumChance;

    private async Task<CraftOperationResult> CommitWithResult(IMongoSession session, CraftOperationResult result)
    {
        return await session.TryCommit(cancelToken.Token)
            ? result
            : await AbortWithResult(session);
    }

    private async Task<CraftOperationResult> AbortWithResult(
        IMongoSession session, CraftResult result = CraftResult.Failed)
    {
        await session.TryAbort(cancelToken.Token);
        return new CraftOperationResult(result);
    }

    private static int CalculateRecipeHash(List<ItemQuantity> inputs)
    {
        var hash = new HashCode();
        var orderByInputs = inputs
            .GroupBy(x => x.Item)
            .Select(g => new ItemQuantity(g.Key, g.Sum(x => x.Quantity)))
            .OrderBy(x => x.Item);

        foreach (var (item, quantity) in orderByInputs)
        {
            hash.Add(item);
            hash.Add(quantity);
        }

        return hash.ToHashCode();
    }
}

public record CraftOperationResult(
    CraftResult CraftResult,
    ItemQuantity? CraftReward = null
);

public enum CraftResult
{
    Failed,
    Success,
    PremiumSuccess,
    NoRecipe,
    NoItems
}