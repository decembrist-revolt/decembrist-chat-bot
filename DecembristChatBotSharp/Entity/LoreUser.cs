using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record LoreUser(
    [property: BsonId] CompositeId Id
);

public record LoreRecord(
    [property: BsonId] LoreRecord.CompositeId Id,
    long[] authorIds,
    string Content
)
{
    public record CompositeId(long ChatId, string Key)
    {
        public static implicit operator CompositeId((long, string) tuple) => new(tuple.Item1, tuple.Item2);
    }
};