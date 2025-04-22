﻿namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

public interface ICommandHandler
{
    public string Command { get; }
    public string Description { get; }
    public CommandLevel CommandLevel { get; }
    public Task<Unit> Do(ChatMessageHandlerParams parameters);
}