using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand.Items;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class CraftService(AppConfig appConfig, MongoDatabase db)
{
    private readonly List<CraftRecipe> _itemList = appConfig.CraftConfig.Recipes;

    public async Task<CraftOperationResult> HandleCraft(List<InputItem> input, long chatId, long telegramId)
    {
        //todo optimize search
        if (_itemList.Any(x => x.Inputs == input)) return new(CraftResult.NoRecipe);

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
    (MemberItemType, int) CraftItem = default
);

public enum CraftResult
{
    Failed,
    Success,
    NoRecipe,
    NoItems
}