using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record LorUser(
    [property: BsonId] CompositeId Id
);

public record LorRecord(
    [property: BsonId] LorRecord.CompositeId Id,
    long[] authorsId,
    string Content
)
{
    public record CompositeId(long ChatId, string Record)
    {
        public static implicit operator CompositeId((long, string) tuple) => new(tuple.Item1, tuple.Item2);
    }
};