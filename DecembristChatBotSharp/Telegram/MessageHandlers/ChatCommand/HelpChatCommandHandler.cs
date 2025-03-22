﻿using System.Text;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class HelpChatCommandHandler(
    CommandLockRepository lockRepository,
    BotClient botClient,
    ExpiredMessageRepository expiredMessageRepository,
    Lazy<IList<ICommandHandler>> commandHandlers) : ICommandHandler
{
    public string Command => "/help";
    public string Description => "Help";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var chatId = parameters.ChatId;
        if (!await lockRepository.TryAcquire(chatId, Command)) return unit;
        
        await TryAsync(botClient.DeleteMessage(chatId, parameters.MessageId)).Match(
            _ => Log.Information("Deleted help message in chat {0}", chatId),
            ex => Log.Error(ex, "Failed to delete help message in chat {0}", chatId)
        );

        var builder = new StringBuilder();
        builder.AppendLine("Available commands:");
        foreach (var handler in commandHandlers.Value)
        {
            builder.AppendLine($"{handler.Command} - {handler.Description}");
        }

        return await botClient.SendMessage(chatId, builder.ToString()).ToTryAsync().Match(
            message =>
            {
                Log.Information("Sent help message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send help message to chat {0}", chatId)
        );
    }
}