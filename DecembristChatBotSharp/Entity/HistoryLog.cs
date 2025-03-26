using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record HistoryLog<T>(
    [property: BsonId] HistoryLog<T>.CompositeId Id,
    HistoryLogType Type,
    T Data) where T : IHistoryLogData
{
    public record CompositeId(
        long ChatId,
        long TelegramId,
        DateTime Timestamp);
}

public enum HistoryLogType
{
    MemberItem = 0,
    TopLiker = 1
}

public interface IHistoryLogData;