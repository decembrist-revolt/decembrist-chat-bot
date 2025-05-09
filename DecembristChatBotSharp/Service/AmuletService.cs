using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class AmuletService(
    MemberItemRepository memberItemRepository,
    HistoryLogRepository historyLogRepository)
{
    public async Task<bool> RemoveAmuletIfExists(long chatId, long receiverId, IMongoSession session)
    {
        var hasAmulet = await memberItemRepository.RemoveMemberItem(chatId, receiverId, MemberItemType.Amulet, session);
        if (hasAmulet)
            await historyLogRepository.LogItem(chatId, receiverId, MemberItemType.Amulet, -1,
                MemberItemSourceType.Use, session);
        return hasAmulet;
    }
}