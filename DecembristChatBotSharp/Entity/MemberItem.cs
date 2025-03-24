﻿namespace DecembristChatBotSharp.Entity;

public record MemberItem(
    MemberItem.CompositeId Id,
    int Count
)
{
    public record CompositeId(long TelegramId, long ChatId, MemberItemType Type);
}
    
public enum MemberItemType
{
    RedditMeme = 0,
    FastReply = 1,
    Box = 2
}

public record MemberItemHistoryLogData(
    MemberItemType MemberItemType,
    int Count,
    MemberItemSourceType SourceType,
    long? SourceTelegramId
) : IHistoryLogData;

public enum MemberItemSourceType
{
    Admin,
    Box,
    Use
}