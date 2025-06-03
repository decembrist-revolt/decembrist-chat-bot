using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;

namespace DecembristChatBotSharp.Service;

public record UniqueItem(
    long ChatId,
    MemberItemType Type,
    long TelegramId,
    DateTime GiveExpiration);

[Singleton]
public class UniqueItemService(
    MemberItemRepository memberItemRepository,
    CancellationTokenSource cancelToken)
{
    public async Task<bool> AddUniqueItem(long chatId, long telegramId, MemberItemType type, IMongoSession session)
    {
        return false;
    }
}