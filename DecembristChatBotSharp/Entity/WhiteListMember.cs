using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record WhiteListMember(
    [property: BsonId] CompositeId Id
);