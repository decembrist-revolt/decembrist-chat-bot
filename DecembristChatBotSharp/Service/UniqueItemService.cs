using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class UniqueItemService(
    UniqueItemRepository uniqueItemRepository,
    AppConfig appConfig)
{
    public async Task<bool> ChangeOwnerUniqueItem(
        long chatId, long telegramId, MemberItemType itemType, IMongoSession session)
    {
        var expiredAt = itemType switch
        {
            MemberItemType.Stone => DateTime.UtcNow.AddMinutes(appConfig.ItemConfig.UniqueItemGiveExpirationMinutes),
            _ => throw new ArgumentOutOfRangeException(nameof(itemType), itemType, null)
        };

        var uniqueItem = new UniqueItem((chatId, itemType), telegramId, expiredAt);
        return await uniqueItemRepository.ChangeOwnerUniqueItem(uniqueItem, session);
    }

    public async Task<Option<int>> GetRemainingTime(UniqueItem.CompositeId id)
    {
        var expireAt = await uniqueItemRepository.GetExpirationTime(id);
        return expireAt.Bind(date => Some((int)(date - DateTime.UtcNow).TotalMinutes));
    }
}