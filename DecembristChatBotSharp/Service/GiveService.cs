using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class GiveService(
    MongoDatabase db,
    AdminUserRepository adminUserRepository,
    MemberItemRepository memberItemRepository,
    HistoryLogRepository historyLogRepository,
    MemberItemService memberItemService,
    CancellationTokenSource cancelToken)
{
    public async Task<GiveOperationResult> GiveItem(long chatId, long senderId, long receiverId,
        ItemQuantity itemQuantity)
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        var isAdmin = await adminUserRepository.IsAdmin((senderId, chatId));
        if (isAdmin) return await GiveItemAdmin(chatId, senderId, receiverId, itemQuantity, session);

        var hasItem = await IsUserHasItem(chatId, senderId, itemQuantity, session);
        if (!hasItem) return await AbortWithResult(session, GiveResult.NoItems);

        var result =
            await IsGiveSuccess(session, chatId, senderId, receiverId, itemQuantity, MemberItemSourceType.Give);
        return result.GiveResult != GiveResult.Failed
            ? await CommitWithResult(session, result)
            : await AbortWithResult(session);
    }

    private async Task<GiveOperationResult> GiveItemAdmin(
        long chatId, long senderId, long receiverId, ItemQuantity itemQuantity, IMongoSession session)
    {
        var result =
            await IsGiveSuccess(session, chatId, senderId, receiverId, itemQuantity, MemberItemSourceType.Admin);
        return result.GiveResult != GiveResult.Failed
            ? await CommitWithResult(session, result)
            : await AbortWithResult(session);
    }

    private async Task<GiveOperationResult> IsGiveSuccess(IMongoSession session,
        long chatId, long senderId, long receiverId, ItemQuantity itemQuantity, MemberItemSourceType sourceType)
    {
        var (item, quantity) = itemQuantity;
        var isAmuletActivated = item == MemberItemType.Amulet &&
                                await memberItemService.HandleAmuletItem((receiverId, chatId), session);
        quantity = isAmuletActivated ? quantity - 1 : quantity;

        var success = (isAmuletActivated && quantity <= 0) ||
                      await memberItemRepository.AddMemberItem(chatId, receiverId, item, session, quantity);
        if (success)
        {
            await historyLogRepository.LogItem(chatId, receiverId, item, quantity, sourceType, session, senderId);
        }

        return success
            ? new GiveOperationResult(GiveResult.Success, isAmuletActivated)
            : new GiveOperationResult(GiveResult.Failed);
    }

    private async Task<bool> IsUserHasItem(long chatId, long senderId, ItemQuantity itemQuantity, IMongoSession session)
    {
        var (item, quantity) = itemQuantity;
        var isHas = await memberItemRepository.IsUserHasItem(chatId, senderId, item, session, quantity) &&
                    await memberItemRepository.RemoveMemberItem(chatId, senderId, item, session, -quantity);
        if (isHas)
        {
            await historyLogRepository.LogItem(
                chatId, senderId, item, -quantity, MemberItemSourceType.Give, session, senderId);
        }

        return isHas;
    }

    private async Task<GiveOperationResult> CommitWithResult(IMongoSession session, GiveOperationResult result) =>
        await session.TryCommit(cancelToken.Token)
            ? result
            : await AbortWithResult(session);

    private async Task<GiveOperationResult> AbortWithResult
        (IMongoSession session, GiveResult result = GiveResult.Failed)
    {
        await session.TryAbort(cancelToken.Token);
        return new GiveOperationResult(result);
    }
}

public record GiveOperationResult(GiveResult GiveResult, bool IsAmuletBroken = false);

public enum GiveResult
{
    NoItems,
    Success,
    AdminSuccess,
    Failed
}