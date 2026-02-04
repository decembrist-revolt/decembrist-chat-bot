using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class UniqueItemService(
    UniqueItemRepository uniqueItemRepository,
    ChatConfigService chatConfigService)
{
    public async Task<bool> ChangeOwnerUniqueItem(
        long chatId, long telegramId, MemberItemType itemType, IMongoSession session)
    {
        var maybeItemConfig = await chatConfigService.GetConfig(chatId, config => config.ItemConfig);
        if (!maybeItemConfig.TryGetSome(out var itemConfig))
        {
            return chatConfigService.LogNonExistConfig(false, nameof(ItemConfig));
        }

        var expiredAt = itemType switch
        {
            MemberItemType.Stone => DateTime.UtcNow.AddMinutes(itemConfig.UniqueItemGiveExpirationMinutes),
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