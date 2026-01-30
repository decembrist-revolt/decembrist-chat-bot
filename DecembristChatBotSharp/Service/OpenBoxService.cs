using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using LanguageExt.UnsafeValueAccess;
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

        return itemType switch
        {
            MemberItemType.Stone => await HandleStone(chatId, telegramId, itemType, session),
            MemberItemType.Amulet => await HandleAmulet(chatId, telegramId, itemType, quantity, session),
            MemberItemType.Box =>
                await HandleItemType(chatId, telegramId, itemType, quantity, OpenBoxResult.SuccessX2, session),
            _ => await HandleItemType(chatId, telegramId, itemType, quantity, OpenBoxResult.Success, session)
        };
    }

    private async Task<OpenBoxResultData> HandleStone(
        long chatId, long telegramId, MemberItemType itemType, IMongoSession session)
    {
        var maybeMinion = await minionService.GetMinionId(telegramId, chatId);
        return telegramId switch
        {
            _ when maybeMinion.TryGetSome(out var minionId) =>
                await HandleStoneForMaster(chatId, telegramId, minionId, itemType, session),
            _ => await HandleUniqueItem(chatId, telegramId, itemType, session)
        };
    }

    private async Task<OpenBoxResultData> HandleAmulet(long chatId, long telegramId, MemberItemType itemType,
        int quantity, IMongoSession session)
    {
        var maybeMaster = await minionService.GetMasterId(telegramId, chatId);
        return telegramId switch
        {
            _ when maybeMaster.TryGetSome(out var masterId) =>
                await HandleAmuletForMinion(chatId, telegramId, masterId, session, itemType),
            _ when await memberItemService.HandleAmuletItem((telegramId, chatId), session) =>
                await HandleItemType(chatId, telegramId, itemType, 0, OpenBoxResult.AmuletActivated, session),
            _ => await HandleItemType(chatId, telegramId, itemType, quantity, OpenBoxResult.Success, session)
        };
    }

    private async Task<OpenBoxResultData> HandleAmuletForMinion(
        long chatId, long minionId, long masterId, IMongoSession session, MemberItemType itemType)
    {
        var success = await minionService.TransferAmuletToMaster(chatId, minionId, masterId, session);
        return success
            ? await HandleItemType(chatId, minionId, itemType, 0, OpenBoxResult.AmuletActivated, session)
            : await AbortWithResult(session);
    }

    private async Task<OpenBoxResultData> HandleUniqueItem(
        long chatId, long telegramId, MemberItemType itemType, IMongoSession session)
    {
        var isHasUniqueItem = await memberItemRepository.IsUserHasItem(chatId, telegramId, itemType, session);
        if (isHasUniqueItem) return await HandleCompensation(chatId, telegramId, itemType, session);

        var isChangeOwner = await memberItemRepository.RemoveAllItemsForChat(chatId, itemType, session)
                            && await uniqueItemService.ChangeOwnerUniqueItem(chatId, telegramId, itemType, session);
        return isChangeOwner
            ? await HandleItemType(chatId, telegramId, itemType, 1, OpenBoxResult.SuccessUnique, session)
            : await AbortWithResult(session);
    }

    private async Task<OpenBoxResultData> HandleStoneForMaster(
        long chatId, long masterId, long minionId, MemberItemType itemType, IMongoSession session)
    {
        var isHasUniqueItem = await memberItemRepository.IsUserHasItem(chatId, minionId, itemType, session);
        if (isHasUniqueItem) return await HandleCompensation(chatId, masterId, itemType, session);

        var isChangeOwner = await memberItemRepository.RemoveAllItemsForChat(chatId, itemType, session)
                            && await uniqueItemService.ChangeOwnerUniqueItem(chatId, minionId, itemType, session);
        return isChangeOwner
            ? await HandleItemType(chatId, minionId, itemType, 1, OpenBoxResult.MinionTransferred, session)
            : await AbortWithResult(session);
    }

    private async Task<OpenBoxResultData> HandleCompensation(
        long chatId, long masterId, MemberItemType itemType, IMongoSession session)
    {
        var compensation = appConfig.ItemConfig.CompensationItem;
        Log.Information("User has unique {0}, compensating item: {1} has been issued", itemType, compensation);
        return await LogInHistoryAndCommit(chatId, masterId, OpenBoxResult.Success, compensation, session, 1);
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