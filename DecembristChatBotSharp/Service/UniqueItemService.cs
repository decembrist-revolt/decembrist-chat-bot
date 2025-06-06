using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;

namespace DecembristChatBotSharp.Service;

public record UniqueItem(
    UniqueItem.CompositeId Id,
    long OwnerId,
    DateTime GiveExpiration)
{
    public record CompositeId(long ChatId, MemberItemType type)
    {
        public static implicit operator CompositeId((long, MemberItemType) tuple) => new(tuple.Item1, tuple.Item2);
    }
};

[Singleton]
public class UniqueItemService(
    MemberItemRepository memberItemRepository,
    UniqueItemRepository uniqueItemRepository,
    CancellationTokenSource cancelToken)
{
    public async Task<bool> ChangeOwnerUniqueItem(
        long chatId, long telegramId, MemberItemType itemType, IMongoSession session) =>
        await uniqueItemRepository.ChangeOwnerUniqueItem((chatId, itemType), telegramId, session);
}