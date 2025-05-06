using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

[BsonIgnoreExtraElements]
public record PollPaymentOffset(long Offset, DateTime LastUpdatedAt);