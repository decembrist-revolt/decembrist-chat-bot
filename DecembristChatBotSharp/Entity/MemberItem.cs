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
    Box = 2
}