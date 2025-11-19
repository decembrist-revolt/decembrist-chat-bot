using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record PremiumMember(
    [property:BsonId]
    CompositeId Id,
    DateTime ExpirationDate,
    int Level = 1
);

public record PremiumMemberHistoryLogData(
    PremiumMemberOperationType OperationType,
    DateTime ExpirationDate,
    int Level,
    long? SourceTelegramId = null,
    string? UserProductId = null
) : IHistoryLogData;

public enum PremiumMemberOperationType
{
    AddByAdmin,
    RemoveByAdmin,
    Payment,
    AddBySlotMachine,
}