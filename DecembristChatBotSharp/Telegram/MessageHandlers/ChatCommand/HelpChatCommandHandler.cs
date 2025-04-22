using System.Text;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class HelpChatCommandHandler(
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    BotClient botClient,
    ExpiredMessageRepository expiredMessageRepository,
    Lazy<IList<ICommandHandler>> commandHandlers) : ICommandHandler
{
    public string Command => "/help";
    public string Description => "Help";
    public CommandLevel CommandLevel => CommandLevel.User;

    private Map<string, string>? _commandDescriptions;

    private Map<string, string> CommandDescriptions => _commandDescriptions ??= commandHandlers.Value
        .Map(handler => (handler.Command, handler.Description))
        .ToMap();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var chatId = parameters.ChatId;

        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        Option<string> maybeMessage;
        if (text.Split("@") is not [_, var subject])
        {
            maybeMessage = await GetCommandsHelp(chatId, parameters.MessageId);
        }
        else if (Enum.TryParse<MemberItemType>(subject, out var itemType))
        {
            maybeMessage = await GetItemHelp(chatId, parameters.MessageId, itemType);
        }
        else
        {
            maybeMessage = await GetCommandsHelp(chatId, parameters.MessageId);
        }

        var sentResult =
            from message in maybeMessage.ToTryOptionAsync()
            from sentMessage in botClient.SendMessage(chatId, message).ToTryOption()
            select fun(() =>
            {
                Log.Information("Sent help message for {0} to chat {1}", text, chatId);
                expiredMessageRepository.QueueMessage(chatId, sentMessage.MessageId);
            });

        return await sentResult.IfFail(ex =>
            Log.Error(ex, "Failed to send help message for {0} to chat {1}", text, chatId));
    }

    private async Task<Option<string>> GetCommandsHelp(long chatId, int messageId)
    {
        if (!await lockRepository.TryAcquire(chatId, Command))
        {
            await messageAssistance.CommandNotReady(chatId, messageId, Command);
            return None;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Available commands:");
        foreach (var (command, description) in CommandDescriptions)
        {
            builder.AppendLine(MakeCommandHelpString(command, description));
        }

        return builder.ToString();
    }

    private string MakeCommandHelpString(string command, string description) => $"{command} - {description}";

    private async Task<Option<string>> GetItemHelp(long chatId, int messageId, MemberItemType itemType)
    {
        var command = $"{Command}={itemType}";
        if (!await lockRepository.TryAcquire(chatId, Command, command))
        {
            await messageAssistance.CommandNotReady(chatId, messageId, command);
            return None;
        }

        Option<string> maybeCommand = itemType switch
        {
            MemberItemType.RedditMeme => RedditMemeCommandHandler.CommandKey,
            MemberItemType.Box => OpenBoxCommandHandler.CommandKey,
            MemberItemType.FastReply => FastReplyCommandHandler.CommandKey,
            MemberItemType.TelegramMeme => TelegramMemeCommandHandler.CommandKey,
            MemberItemType.Curse => ReactionSpamCommandHandler.CommandKey,
            MemberItemType.Charm => CharmCommandHandler.CommandKey,
            _ => None
        };

        return maybeCommand.Map(commandKey => (Command: commandKey, Description: CommandDescriptions[commandKey]))
            .Map(pair => MakeCommandHelpString(pair.Command, pair.Description))
            .Map(help => $"Item {itemType} help:\n\n{help}");
    }
}