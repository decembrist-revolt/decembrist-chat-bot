using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using LanguageExt.SomeHelp;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class DustService(
    MemberItemRepository memberItemRepository,
    PremiumMemberRepository premiumMemberRepository,
    HistoryLogRepository historyLogRepository,
    AppConfig appConfig,
    Random random,
    MongoDatabase db,
    CancellationTokenSource cancelToken)
{
    private readonly IReadOnlyDictionary<MemberItemType, DustRecipe> _recipes = appConfig.DustRecipesConfig.DustRecipes;

    public async Task<DustOperationResult> HandleDust(MemberItemType item, long chatId, long telegramId)
    {
        if (!_recipes.TryGetValue(item, out var recipe)) return new DustOperationResult(DustResult.NoRecipe);

        using var session = await db.OpenSession();
        session.StartTransaction();

        var hasItem = await memberItemRepository.RemoveMemberItem(chatId, telegramId, item, session);
        return hasItem
            ? await ProcessDustOperation(item, chatId, telegramId, session, recipe)
            : await AbortWithResult(session, DustResult.NoItems);
    }

    private async Task<DustOperationResult> ProcessDustOperation(
        MemberItemType item, long chatId, long telegramId, IMongoSession session, DustRecipe recipe)
    {
        var result = await ProcessRewards(chatId, telegramId, recipe, session);

        return result.Result switch
        {
            DustResult.PremiumSuccess or DustResult.Success =>
                await LogInHistoryAndCommit(result, session, chatId, telegramId, item),
            _ => await AbortWithResult(session, result.Result)
        };
    }

    private async Task<DustOperationResult> LogInHistoryAndCommit(
        DustOperationResult result, IMongoSession session, long chatId, long telegramId, MemberItemType removeItem)
    {
        var list = new List<ItemQuantity>();
        if (!result.DustReward.IsNull()) list.Add(result.DustReward!);
        if (!result.PremiumReward.IsNull()) list.Add(result.PremiumReward!);
        list.Add(new ItemQuantity(removeItem, -1));

        await historyLogRepository.LogDifferentItems(chatId, telegramId, list, session, MemberItemSourceType.Dust);
        return await session.TryCommit(cancelToken.Token) ? result : await AbortWithResult(session);
    }

    private async Task<DustOperationResult> ProcessRewards(
        long chatId, long telegramId, DustRecipe recipe, IMongoSession session)
    {
        var dustReward = GetReward(recipe.Reward);
        var success = await AddRewardItems(chatId, telegramId, dustReward, session);
        var isPremium = await premiumMemberRepository.IsPremium((telegramId, chatId));
        var maybeBonus =
            isPremium && recipe.PremiumReward != null ? TryGetPremiumReward(recipe.PremiumReward) : None;

        if (!success) return new DustOperationResult(DustResult.Failed);

        return await maybeBonus.MatchAsync(
            None: () => new DustOperationResult(DustResult.Success, dustReward),
            Some: async premiumReward =>
            {
                var premiumSuccess = await AddRewardItems(chatId, telegramId, premiumReward, session);
                return premiumSuccess
                    ? new DustOperationResult(DustResult.PremiumSuccess, dustReward, premiumReward)
                    : new DustOperationResult(DustResult.Failed);
            }
        );
    }

    private async Task<DustOperationResult> AbortWithResult(IMongoSession session,
        DustResult result = DustResult.Failed)
    {
        await session.TryAbort(cancelToken.Token);
        return new DustOperationResult(result);
    }

    private async Task<bool> AddRewardItems(
        long chatId, long telegramId, ItemQuantity items, IMongoSession session)
    {
        var (item, quantity) = items;
        return await memberItemRepository.AddMemberItem(chatId, telegramId, item, session, quantity);
    }

    private Option<ItemQuantity> TryGetPremiumReward(PremiumReward premiumReward) =>
        random.NextDouble() < premiumReward.Chance
            ? new ItemQuantity(premiumReward.Item, premiumReward.Quantity).ToSome()
            : None;

    private ItemQuantity GetReward(DustReward reward)
    {
        var quantity = random.Next(reward.Range.Min, reward.Range.Max + 1);
        return new ItemQuantity(reward.Item, quantity);
    }
}

public record DustOperationResult(
    DustResult Result,
    ItemQuantity? DustReward = null,
    ItemQuantity? PremiumReward = null);

public enum DustResult
{
    Success,
    PremiumSuccess,
    NoRecipe,
    NoItems,
    Failed,
}