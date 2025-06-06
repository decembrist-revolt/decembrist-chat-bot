using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class GiveService(
    MongoDatabase db,
    AdminUserRepository adminUserRepository,
    UniqueItemRepository uniqueItemRepository,
    MemberItemRepository memberItemRepository,
    HistoryLogRepository historyLogRepository,
    MemberItemService memberItemService,
    CancellationTokenSource cancelToken)
{
    public async Task<GiveOperationResult> GiveItem(
        long chatId, long senderId, long receiverId, ItemQuantity itemQuantity)
    {
        var isAdmin = await adminUserRepository.IsAdmin((senderId, chatId));
        if (!isAdmin && senderId == receiverId) return new GiveOperationResult(GiveResult.Self);
        using var session = await db.OpenSession();
        session.StartTransaction();

        if (isAdmin) return await GiveItemAdmin(chatId, senderId, receiverId, itemQuantity, session, isAdmin);

        var hasItem = await IsUserHasItem(chatId, senderId, itemQuantity, session);
        if (!hasItem) return await AbortWithResult(session, GiveResult.NoItems);

        var result = await IsGiveSuccess(session, chatId, senderId, receiverId, itemQuantity);
        return result.GiveResult != GiveResult.Failed
            ? await CommitWithResult(session, result)
            : await AbortWithResult(session);
    }

    private async Task<GiveOperationResult> GiveItemAdmin(
        long chatId, long senderId, long receiverId, ItemQuantity itemQuantity, IMongoSession session, bool isAdmin)
    {
        var result = await IsGiveSuccess(session, chatId, senderId, receiverId, itemQuantity, isAdmin);
        return result.GiveResult != GiveResult.Failed
            ? await CommitWithResult(session, result with { GiveResult = GiveResult.AdminSuccess })
            : await AbortWithResult(session);
    }

    private async Task<GiveOperationResult> IsGiveSuccess(IMongoSession session, long chatId, long senderId,
        long receiverId, ItemQuantity itemQuantity, bool isAdmin = false)
    {
        var (item, quantity) = itemQuantity;
        var success = item switch
        {
            MemberItemType.Amulet => await GiveAmulet(chatId, receiverId, item, quantity, session),
            MemberItemType.Stone => await GiveUnique(chatId, receiverId, item, quantity, session, isAdmin),
            _ => await GiveDefault(chatId, receiverId, item, quantity, session)
        };

        if (success.GiveResult == GiveResult.Success)
        {
            var sourceType = isAdmin ? MemberItemSourceType.Admin : MemberItemSourceType.Give;
            await historyLogRepository.LogItem(chatId, receiverId, item, quantity, sourceType, session, senderId);
        }

        return success;
    }

    private async Task<GiveOperationResult> GiveAmulet(long chatId, long receiverId, MemberItemType item, int quantity,
        IMongoSession session)
    {
        var isAmuletExist = await memberItemService.HandleAmuletItem((receiverId, chatId), session);
        if (isAmuletExist)
        {
            quantity -= 1;
            if (quantity <= 0) return new GiveOperationResult(GiveResult.Success, isAmuletExist);
        }

        var result = await GiveDefault(chatId, receiverId, item, quantity, session);
        return isAmuletExist ? result with { IsAmuletBroken = isAmuletExist } : result;
    }

    private async Task<GiveOperationResult> GiveUnique(
        long chatId, long receiverId, MemberItemType item, int quantity, IMongoSession session, bool isAdmin)
    {
        if (isAdmin) await memberItemRepository.RemoveAllItemsForChat(chatId, item, session);
        var isChangeOwner = await uniqueItemRepository.IsGiveExpired((chatId, item), session);
        if (!isChangeOwner) return new GiveOperationResult(GiveResult.NotExpired);

        var isChange = await uniqueItemRepository.ChangeOwnerUniqueItem((chatId, item), receiverId, session);
        return isChange
            ? await GiveDefault(chatId, receiverId, item, quantity, session)
            : new GiveOperationResult(GiveResult.Failed);
    }

    private async Task<GiveOperationResult> GiveDefault(
        long chatId, long receiverId, MemberItemType item, int quantity, IMongoSession session)
    {
        var success = await memberItemRepository.AddMemberItem(chatId, receiverId, item, session, quantity);
        return new GiveOperationResult(success ? GiveResult.Success : GiveResult.Failed);
    }

    private async Task<bool> IsUserHasItem(long chatId, long senderId, ItemQuantity itemQuantity, IMongoSession session)
    {
        var (item, quantity) = itemQuantity;
        var isHas = await memberItemRepository.RemoveMemberItem(chatId, senderId, item, session, -quantity);

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
    NotExpired,
    Success,
    AdminSuccess,
    Failed,
    Self
}