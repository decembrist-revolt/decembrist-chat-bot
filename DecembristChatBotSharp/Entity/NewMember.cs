using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record NewMember(
    [property:BsonId]
    NewMember.CompositeId Id,
    string Username,
    int WelcomeMessageId,
    DateTime EnterDate,
    int CaptchaRetryCount = 0)
{
    public record CompositeId(long TelegramId, long ChatId)
    {
        public static implicit operator CompositeId((long TelegramId, long ChatId) tuple) => 
            new(tuple.TelegramId, tuple.ChatId);
    }
}