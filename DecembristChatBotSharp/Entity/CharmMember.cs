using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record CharmMember(
    [property: BsonId] CompositeId Id,
    string SecretWord,
    DateTime ExpireAt,
    int? SecretMessageId = null
);