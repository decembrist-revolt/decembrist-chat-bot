using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Recipes;
using Lamar;

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
    private readonly Dictionary<MemberItemType, DustRecipe> _recipes = appConfig.DustConfig.DustRecipes;

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
        await historyLogRepository.LogItem(chatId, telegramId, item, -1, MemberItemSourceType.Dust, session);

        var result = await ProcessRewards(chatId, telegramId, recipe, session);
        return result.Result switch
        {
            DustResult.Success or DustResult.PremiumSuccess => await CommitWithResult(session, result),
            _ => await AbortWithResult(session, result.Result)
        };
    }

    private async Task<DustOperationResult> ProcessRewards(
        long chatId, long telegramId, DustRecipe recipe, IMongoSession session)
    {
        var dustReward = GetReward(recipe.Reward);
        var success = await AddRewardItems(chatId, telegramId, dustReward, session);
        var isPremium = await premiumMemberRepository.IsPremium((telegramId, chatId));
        var maybeBonus = isPremium && recipe.PremiumReward != null
            ? TryGetPremiumReward(recipe.PremiumReward)
            : None;

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

    private async Task<DustOperationResult> CommitWithResult(IMongoSession session, DustOperationResult result) =>
        await session.TryCommit(cancelToken.Token)
            ? result
            : await AbortWithResult(session);

    private async Task<bool> AddRewardItems(
        long chatId, long telegramId, (MemberItemType, int) items, IMongoSession session)
    {
        var (item, quantity) = items;
        await historyLogRepository.LogItem(chatId, telegramId, item, quantity, MemberItemSourceType.Dust, session);
        return await memberItemRepository.AddMemberItem(chatId, telegramId, item, session, quantity);
    }

    private Option<(MemberItemType, int)> TryGetPremiumReward(PremiumReward premiumReward)
    {
        return random.NextDouble() < premiumReward.Chance
            ? Some((premiumReward.Item, premiumReward.Quantity))
            : None;
    }

    private (MemberItemType, int) GetReward(DustReward reward)
    {
        var quantity = random.Next(reward.Range.Min, reward.Range.Max + 1);
        return (reward.Item, quantity);
    }
}

public record DustOperationResult(
    DustResult Result,
    (MemberItemType, int) DustReward = default,
    (MemberItemType, int) PremiumReward = default);

public enum DustResult
{
    Success,
    PremiumSuccess,
    NoRecipe,
    NoItems,
    Failed,
}