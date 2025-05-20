using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand.Items;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class CraftService(
    AppConfig appConfig,
    MongoDatabase db,
    HistoryLogRepository historyLogRepository,
    MemberItemRepository memberItemRepository)
{
    private readonly Dictionary<int, CraftRecipe> _recipeCache =
        appConfig.CraftConfig.Recipes.ToDictionary(recipe => CraftRecipe.CalculateRecipeHash(recipe.Inputs),
            recipe => recipe);

    public async Task<CraftOperationResult> HandleCraft(List<InputItem> input, long chatId, long telegramId)
    {
        var inputHash = CraftRecipe.CalculateRecipeHash(input);
        if (!_recipeCache.TryGetValue(inputHash, out var recipe)) return new CraftOperationResult(CraftResult.NoRecipe);

        using var session = await db.OpenSession();
        session.StartTransaction();

        var hasItem = await memberItemRepository.RemoveMemberItem(chatId, telegramId, item, session);
        return hasItem
            ? await ProcessDustOperation(item, chatId, telegramId, session, recipe)
            : await AbortWithResult(session, new CraftOperationResult(CraftResult.NoItems));
    }
}

public record CraftOperationResult(
    CraftResult CraftResult,
    (MemberItemType, int) CraftItem = default);

public enum CraftResult
{
    Failed,
    Success,
    NoRecipe,
    NoItems
}