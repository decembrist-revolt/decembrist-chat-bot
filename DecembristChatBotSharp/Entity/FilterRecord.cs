using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record FilterRecord(
    [property: BsonId] FilterRecord.CompositeId Id
)
{
    public record CompositeId(long ChatId, string Key)
    {
        public static implicit operator CompositeId((long, string) tuple) => new(tuple.Item1, tuple.Item2);
    }
}