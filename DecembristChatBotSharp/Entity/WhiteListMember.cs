using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;
public readonly record struct WhiteListMember(
    [property: BsonId] long Id);
