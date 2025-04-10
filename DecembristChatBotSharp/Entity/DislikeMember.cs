using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record DislikeMember(
    [property: BsonId] CompositeId Id,
    long DislikeTelegramId);

public record DislikesResultGroup(
    long DislikeUserId,
    long[] Dislikers,
    int DislikersCount);

public record DislikeResultHistoryLogData(
    int Dislikes,
    long RandomDislikerId
) : IHistoryLogData;