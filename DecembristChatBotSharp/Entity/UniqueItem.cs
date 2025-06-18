namespace DecembristChatBotSharp.Entity;

public record UniqueItem(
    UniqueItem.CompositeId Id,
    long OwnerId,
    DateTime GiveExpiration)
{
    public record CompositeId(long ChatId, MemberItemType type)
    {
        public static implicit operator CompositeId((long, MemberItemType) tuple) => new(tuple.Item1, tuple.Item2);
    }
}