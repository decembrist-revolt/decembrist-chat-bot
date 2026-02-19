namespace DecembristChatBotSharp.Entity;

public record FilterRestrictUser(
    CompositeId Id,
    DateTime Expired,
    int RestrictMessageId
);