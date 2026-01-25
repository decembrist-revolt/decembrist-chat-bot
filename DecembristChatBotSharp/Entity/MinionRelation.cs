using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record MinionRelation(
    [property: BsonId] CompositeId Id, // Minion's composite ID
    long MasterTelegramId,
    int? ConfirmationMessageId = null,
    DateTime? CreatedAt = null
);
