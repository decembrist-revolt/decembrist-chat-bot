using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Recipes;
using Lamar;

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
        appConfig.CraftConfig.Recipes.ToDictionary(recipe => CalculateRecipeHash(recipe.Inputs), recipe => recipe);

    public async Task<CraftOperationResult> HandleCraft(List<InputItem> input, long chatId, long telegramId)
    {
        var inputHash = CalculateRecipeHash(input);
        if (!_recipeCache.TryGetValue(inputHash, out var recipe)) return new CraftOperationResult(CraftResult.NoRecipe);

        using var session = await db.OpenSession();
        session.StartTransaction();

        var hasItem = await RemoveInputItems(chatId, telegramId, recipe, session);

        return hasItem
            ? await ProcessCraftOperation(recipe.Outputs, chatId, telegramId, session)
            : await AbortWithResult(session, CraftResult.NoItems);
    }

    private async Task<bool> RemoveInputItems(long chatId, long telegramId, CraftRecipe recipe, IMongoSession session)
    {
        var items = recipe.Inputs.Select(x => (x.Item, x.Quantity)).ToMap();
        var hasItem = await memberItemRepository.RemoveMemberItems(chatId, telegramId, items, session);
        if (!hasItem) return false;
        var itemsLog = items.Map(x => (x.Key, -x.Value)).ToMap();
        await historyLogRepository.LogManyItems(chatId, telegramId, itemsLog, session);
        return hasItem;
    }

    private async Task<CraftOperationResult> ProcessCraftOperation(List<OutputItem> recipeOutputs, long chatId,
        long telegramId, IMongoSession session)
    {
        var craftItem = GetRandomOutputItem(recipeOutputs);

        var isGetPremiumBonus = await premiumMemberRepository.IsPremium((telegramId, chatId)) && IsGetBonus();
        if (isGetPremiumBonus) craftItem.Item2++;
        var result = isGetPremiumBonus ? CraftResult.PremiumSuccess : CraftResult.Success;

        var isAdd = await AddCraftItems(chatId, telegramId, craftItem, session);

        return isAdd
            ? await CommitWithResult(session, new CraftOperationResult(result, craftItem))
            : await AbortWithResult(session);
    }

    private async Task<bool> AddCraftItems(
        long chatId, long telegramId, (MemberItemType, int) items, IMongoSession session)
    {
        var (item, quantity) = items;
        await historyLogRepository.LogItem(chatId, telegramId, item, quantity, MemberItemSourceType.Craft, session);
        return await memberItemRepository.AddMemberItem(chatId, telegramId, item, session, quantity);
    }

    private (MemberItemType, int) GetRandomOutputItem(List<OutputItem> outputs)
    {
        if (outputs.Count == 1)
        {
            var first = outputs.First();
            return (first.Item, first.Quantity);
        }

        var total = outputs.Sum(x => x.Chance);
        var roll = random.NextDouble() * total;

        var cumulative = 0.0;

        foreach (var output in outputs)
        {
            cumulative += output.Chance;
            if (roll <= cumulative) return (output.Item, output.Quantity);
        }

        var last = outputs.Last();

        return (last.Item, last.Quantity);
    }

    private bool IsGetBonus() => random.NextDouble() < appConfig.CraftConfig.PremiumChance;

    private async Task<CraftOperationResult> CommitWithResult(IMongoSession session, CraftOperationResult result) =>
        await session.TryCommit(cancelToken.Token)
            ? result
            : await AbortWithResult(session);

    private async Task<CraftOperationResult> AbortWithResult(
        IMongoSession session, CraftResult result = CraftResult.Failed)
    {
        await session.TryAbort(cancelToken.Token);
        return new CraftOperationResult(result);
    }

    private static int CalculateRecipeHash(List<InputItem> inputs)
    {
        var hash = new HashCode();
        var orderByInputs = inputs
            .GroupBy(x => x.Item)
            .Select(g => (Item: g.Key, Quantity: g.Sum(x => x.Quantity)))
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
    (MemberItemType, int) CraftReward = default);

public enum CraftResult
{
    Failed,
    Success,
    PremiumSuccess,
    NoRecipe,
    NoItems
}