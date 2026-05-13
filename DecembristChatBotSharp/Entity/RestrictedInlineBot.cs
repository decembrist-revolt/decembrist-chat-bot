using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record RestrictedInlineBot(
    [property: BsonId] RestrictedInlineBotId Id
);

public record RestrictedInlineBotId(long ChatId, string Username);
