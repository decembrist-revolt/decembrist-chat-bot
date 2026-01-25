using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

/// <summary>
/// Represents a pending minion invitation from a premium member to another user
/// </summary>
public record MinionInvitation(
    [property: BsonId]
    CompositeId Id,
    long MasterTelegramId,
    int InvitationMessageId,
    DateTime CreatedAt,
    DateTime? ExpiresAt = null
);
