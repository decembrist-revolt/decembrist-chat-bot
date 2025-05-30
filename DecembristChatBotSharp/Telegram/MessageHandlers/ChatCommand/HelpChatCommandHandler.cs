using System.Text;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Items;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class HelpChatCommandHandler(
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    AppConfig appConfig,
    Lazy<IList<ICommandHandler>> commandHandlers,
    Lazy<IList<IPassiveItem>> passiveItems) : ICommandHandler
{
    public string Command => "/help";
    public string Description => "Help";
    public CommandLevel CommandLevel => CommandLevel.User;

    private Map<string, string>? _commandDescriptions;

    private Map<string, string> CommandDescriptions => _commandDescriptions ??= commandHandlers.Value
        .Filter(handler => handler.CommandLevel != CommandLevel.Admin)
        .Map(handler => (handler.Command, handler.Description))
        .ToMap();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, _, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        Task<Unit> helpTask;
        if (text.Contains('@') && text.Split('@') is [.._, var subject] &&
            !subject.EndsWith("bot", StringComparison.OrdinalIgnoreCase))
        {
            helpTask = GetSpecificHelp(chatId, messageId, subject.ToLower());
        }
        else
        {
            helpTask = GetCommandsHelp(chatId, messageId);
        }

        return await Array(helpTask,
            messageAssistance.DeleteCommandMessage(chatId, parameters.MessageId, Command)).WhenAll();
    }

    private async Task<Unit> GetCommandsHelp(long chatId, int messageId)
    {
        if (!await lockRepository.TryAcquire(chatId, Command))
        {
            return await messageAssistance.CommandNotReady(chatId, messageId, Command);
        }

        var builder = new StringBuilder();
        builder.AppendLine(appConfig.HelpConfig.HelpTitle);
        foreach (var (command, description) in CommandDescriptions)
        {
            builder.AppendLine(MakeCommandHelpString(command, description));
        }

        return await messageAssistance.SendCommandResponse(chatId, builder.ToString(), Command);
    }

    private async Task<Unit> GetSpecificHelp(long chatId, int messageId, string subject)
    {
        var command = $"{Command}={subject}";
        if (!await lockRepository.TryAcquire(chatId, Command, command))
        {
            return await messageAssistance.CommandNotReady(chatId, messageId, command);
        }

        if (!Enum.TryParse(subject, true, out MemberItemType itemType))
            return await GetCommandHelp(chatId, AddCommandPrefix(subject));

        var maybePassiveItem = passiveItems.Value.Find(x => x.ItemType == itemType);
        return await maybePassiveItem.MatchAsync(
            Some: passiveItem => SendItemHelp(chatId, passiveItem.ItemType, passiveItem.Description),
            None: () => GetActiveItemHelp(chatId, AddCommandPrefix(subject), itemType));
    }

    private Task<Unit> GetActiveItemHelp(long chatId, string command, MemberItemType itemType)
    {
        command = itemType switch
        {
            MemberItemType.Box => OpenBoxCommandHandler.CommandKey,
            _ => command
        };
        return CommandDescriptions.Find(command).Match(
            Some: description => SendItemHelp(chatId, itemType, MakeCommandHelpString(command, description)),
            None: () => SendHelpNotFound(chatId, command));
    }

    private Task<Unit> GetCommandHelp(long chatId, string command) =>
        CommandDescriptions.Find(command).Match(
            Some: description => SendCommandHelp(chatId, command, MakeCommandHelpString(command, description)),
            None: () => SendHelpNotFound(chatId, command));

    private Task<Unit> SendCommandHelp(long chatId, string command, string description)
    {
        var message = string.Format(appConfig.HelpConfig.CommandHelpTemplate, command, description);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendItemHelp(long chatId, MemberItemType item, string description)
    {
        var message = string.Format(appConfig.HelpConfig.ItemHelpTemplate, item, description);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendHelpNotFound(long chatId, string subject)
    {
        Log.Information("Help not found for: {0}", subject);
        return messageAssistance.SendCommandResponse(chatId, appConfig.HelpConfig.FailedMessage, Command);
    }

    private string MakeCommandHelpString(string command, string description) => $"{command} - {description}";
    private string AddCommandPrefix(string subject) => "/" + subject;
}