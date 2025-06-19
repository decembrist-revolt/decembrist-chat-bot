using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class OpenBoxService(
    MemberItemRepository memberItemRepository,
    MemberItemService memberItemService,
    AppConfig appConfig,
    MongoDatabase db,
    HistoryLogRepository historyLogRepository,
    Random random,
    CancellationTokenSource cancelToken,
    UniqueItemService uniqueItemService)
{
    public async Task<(Option<MemberItemType>, OpenBoxResult)> OpenBox(long chatId, long telegramId)
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        var hasBox = await memberItemRepository.RemoveMemberItem(chatId, telegramId, MemberItemType.Box, session);
        if (!hasBox) return await AbortWithResult(session, OpenBoxResult.NoItems);

        var itemType = GetRandomItem();
        return itemType switch
        {
            MemberItemType.Stone => await HandleUniqueItem(chatId, telegramId, itemType, session),
            MemberItemType.Box =>
                await HandleItemType(chatId, telegramId, itemType, 2, OpenBoxResult.SuccessX2, session),
            MemberItemType.Amulet when await memberItemService.HandleAmuletItem((telegramId, chatId), session) =>
                await HandleItemType(chatId, telegramId, itemType, 0, OpenBoxResult.AmuletActivated, session),
            _ => await HandleItemType(chatId, telegramId, itemType, 1, OpenBoxResult.Success, session)
        };
    }

    private async Task<(Option<MemberItemType>, OpenBoxResult)> HandleUniqueItem(
        long chatId, long telegramId, MemberItemType itemType, IMongoSession session)
    {
        var isHasUniqueItem = await memberItemRepository.IsUserHasItem(chatId, telegramId, itemType, session);
        if (isHasUniqueItem)
        {
            return await LogInHistoryAndCommit(chatId, telegramId, OpenBoxResult.Success, MemberItemType.RedditMeme,
                session, 1);
        }

        var isChangeOwner = await memberItemRepository.RemoveAllItemsForChat(chatId, itemType, session)
                            && await uniqueItemService.ChangeOwnerUniqueItem(chatId, telegramId, itemType, session);
        return isChangeOwner
            ? await HandleItemType(chatId, telegramId, itemType, 1, OpenBoxResult.SuccessUnique, session)
            : await AbortWithResult(session);
    }

    private async Task<(Option<MemberItemType>, OpenBoxResult)> HandleItemType(
        long chatId, long telegramId, MemberItemType itemType, int numberItems, OpenBoxResult result,
        IMongoSession session)
    {
        var success = numberItems == 0 ||
                      await memberItemRepository.AddMemberItem(chatId, telegramId, itemType, session, numberItems);
        return success
            ? await LogInHistoryAndCommit(chatId, telegramId, result, itemType, session, numberItems)
            : await AbortWithResult(session);
    }

    private async Task<(Option<MemberItemType>, OpenBoxResult)> LogInHistoryAndCommit(long chatId, long telegramId,
        OpenBoxResult result, MemberItemType itemType, IMongoSession session, int countItem)
    {
        var list = new List<ItemQuantity>
        {
            new(MemberItemType.Box, -1),
            new(itemType, countItem)
        };

        await historyLogRepository.LogDifferentItems(chatId, telegramId, list, session, MemberItemSourceType.Box);
        return await CommitWithResult(session, (itemType, result));
    }

    private async Task<(Option<MemberItemType>, OpenBoxResult)> CommitWithResult(IMongoSession session,
        (Option<MemberItemType>, OpenBoxResult) result) =>
        await session.TryCommit(cancelToken.Token)
            ? result
            : await AbortWithResult(session);

    private async Task<(Option<MemberItemType>, OpenBoxResult)> AbortWithResult(
        IMongoSession session, OpenBoxResult result = OpenBoxResult.Failed)
    {
        await session.TryAbort(cancelToken.Token);
        return (None, result);
    }

    private MemberItemType GetRandomItem()
    {
        var itemChances = appConfig.ItemConfig.ItemChance;
        var total = itemChances.Values.Sum();
        var roll = random.NextDouble() * total;

        var cumulative = 0.0;

        foreach (var pair in itemChances)
        {
            cumulative += pair.Value;
            if (roll <= cumulative)
            {
                return pair.Key;
            }
        }

        return itemChances.Keys.First();
    }
}