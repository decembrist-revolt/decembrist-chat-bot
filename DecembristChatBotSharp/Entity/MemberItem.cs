namespace DecembristChatBotSharp.Entity;

public record MemberItem(
    MemberItem.CompositeId Id,
    int Count
)
{
    public record CompositeId(long TelegramId, long ChatId, MemberItemType Type);
}

public enum MemberItemType
{
    RedditMeme = 0,
    FastReply = 1,
    Box = 2,
    TelegramMeme = 3,

    // reaction spam
    Curse = 4,
    Charm = 5,

    // passive
    Amulet = 6,
    GreenDust = 7,
    BlueDust = 8,
    RedDust = 9
}

public record MemberItemHistoryLogData(
    MemberItemType MemberItemType,
    int Count,
    MemberItemSourceType SourceType,
    long? SourceTelegramId
) : IHistoryLogData;

public record ManyItemsHistoryLogData(
    IEnumerable<ItemQuantity> Items,
    MemberItemSourceType SourceType) : IHistoryLogData;

public enum MemberItemSourceType
{
    Admin = 0,
    Box = 1,
    Use = 2,
    TopLiker = 3,
    PremiumDaily = 4,
    Dust = 5,
    Craft = 6
}