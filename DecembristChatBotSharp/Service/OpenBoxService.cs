using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class OpenBoxService(
    MemberItemRepository memberItemRepository,
    MemberItemService memberItemService,
    MinionService minionService,
    AppConfig appConfig,
    MongoDatabase db,
    HistoryLogRepository historyLogRepository,
    Random random,
    CancellationTokenSource cancelToken,
    UniqueItemService uniqueItemService)
{
    public async Task<OpenBoxResultData> OpenBox(long chatId, long telegramId)
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        var hasBox = await memberItemRepository.RemoveMemberItem(chatId, telegramId, MemberItemType.Box, session);
        if (!hasBox) return await AbortWithResult(session, OpenBoxResult.NoItems);

        var (itemType, quantity) = GetRandomItemWithQuantity();
        
        // Check if user is a minion and handle amulet transfer
        if (itemType == MemberItemType.Amulet)
        {
            var masterId = await minionService.GetMasterId(telegramId, chatId);
            if (masterId.IsSome)
            {
                // Minion got amulet - transfer to master
                var success = await memberItemService.HandleAmuletItem((telegramId, chatId), session);
                if (success)
                {
                    await minionService.TransferAmuletToMaster(telegramId, chatId, session);
                }
                return await HandleItemType(chatId, telegramId, itemType, 0, OpenBoxResult.AmuletActivated, session);
            }

            if (await memberItemService.HandleAmuletItem((telegramId, chatId), session))
            {
                return await HandleItemType(chatId, telegramId, itemType, 0, OpenBoxResult.AmuletActivated, session);
            }
        }

        // Check if user has a minion and handle stone transfer
        if (itemType == MemberItemType.Stone)
        {
            var minionId = await minionService.GetMinionId(telegramId, chatId);
            if (minionId.IsSome)
            {
                // Master got stone - transfer to minion
                await minionService.TransferStoneToMinion(telegramId, chatId, session);
                return await HandleUniqueItem(chatId, minionId.IfNone(telegramId), itemType, session);
            }
        }
        
        return itemType switch
        {
            MemberItemType.Stone => await HandleUniqueItem(chatId, telegramId, itemType, session),
            MemberItemType.Box =>
                await HandleItemType(chatId, telegramId, itemType, quantity, OpenBoxResult.SuccessX2, session),
            MemberItemType.Amulet when await memberItemService.HandleAmuletItem((telegramId, chatId), session) =>
                await HandleItemType(chatId, telegramId, itemType, 0, OpenBoxResult.AmuletActivated, session),
            _ => await HandleItemType(chatId, telegramId, itemType, quantity, OpenBoxResult.Success, session)
        };
    }

    private async Task<OpenBoxResultData> HandleUniqueItem(
        long chatId, long telegramId, MemberItemType itemType, IMongoSession session)
    {
        var isHasUniqueItem = await memberItemRepository.IsUserHasItem(chatId, telegramId, itemType, session);
        if (isHasUniqueItem)
        {
            var compensation = appConfig.ItemConfig.CompensationItem;
            Log.Information("User has unique {0}, compensating item: {1} has been issued", itemType, compensation);
            return await LogInHistoryAndCommit(chatId, telegramId, OpenBoxResult.Success, compensation, session, 1);
        }

        var isChangeOwner = await memberItemRepository.RemoveAllItemsForChat(chatId, itemType, session)
                            && await uniqueItemService.ChangeOwnerUniqueItem(chatId, telegramId, itemType, session);
        return isChangeOwner
            ? await HandleItemType(chatId, telegramId, itemType, 1, OpenBoxResult.SuccessUnique, session)
            : await AbortWithResult(session);
    }

    private async Task<OpenBoxResultData> HandleItemType(
        long chatId, long telegramId, MemberItemType itemType, int numberItems, OpenBoxResult result,
        IMongoSession session)
    {
        var success = numberItems == 0 ||
                      await memberItemRepository.AddMemberItem(chatId, telegramId, itemType, session, numberItems);
        return success
            ? await LogInHistoryAndCommit(chatId, telegramId, result, itemType, session, numberItems)
            : await AbortWithResult(session);
    }

    private async Task<OpenBoxResultData> LogInHistoryAndCommit(long chatId, long telegramId,
        OpenBoxResult result, MemberItemType itemType, IMongoSession session, int countItem)
    {
        var list = new List<ItemQuantity>
        {
            new(MemberItemType.Box, -1),
            new(itemType, countItem)
        };

        await historyLogRepository.LogDifferentItems(chatId, telegramId, list, session, MemberItemSourceType.Box);
        return await CommitWithResult(session, new OpenBoxResultData(itemType, countItem, result));
    }

    private async Task<OpenBoxResultData> CommitWithResult(IMongoSession session,
        OpenBoxResultData result) =>
        await session.TryCommit(cancelToken.Token)
            ? result
            : await AbortWithResult(session);

    private async Task<OpenBoxResultData> AbortWithResult(
        IMongoSession session, OpenBoxResult result = OpenBoxResult.Failed)
    {
        await session.TryAbort(cancelToken.Token);
        return new OpenBoxResultData(None, 0, result);
    }

    private (MemberItemType, int) GetRandomItemWithQuantity()
    {
        var itemChances = appConfig.ItemConfig.ItemChance;
        var total = itemChances.Values.Sum(x => x.Chance);
        var roll = random.NextDouble() * total;

        var cumulative = 0.0;

        foreach (var pair in itemChances)
        {
            cumulative += pair.Value.Chance;
            if (roll <= cumulative)
            {
                return (pair.Key, pair.Value.Quantity);
            }
        }

        var first = itemChances.First();
        return (first.Key, first.Value.Quantity);
    }
}