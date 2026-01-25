using System.Text;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Items;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class HelpChatCommandHandler(
    AppConfig appConfig,
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    Lazy<IList<ICommandHandler>> commandHandlers,
    Lazy<IList<IPassiveItem>> passiveItems,
    ChatConfigService chatConfigService) : ICommandHandler
{
    public string Command => "/help";
    public string Description => appConfig.CommandConfig.CommandDescriptions.GetValueOrDefault(Command, "Help");
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
        var maybeHelpConfig = await chatConfigService.GetConfig(chatId, config => config.HelpConfig);
        if (!maybeHelpConfig.TryGetSome(out var helpConfig))
            return await messageAssistance.DeleteCommandMessage(chatId, parameters.MessageId, Command);

        Task<Unit> helpTask;
        if (text.Contains('@') && text.Split('@') is [.. _, var subject] &&
            !subject.EndsWith("bot", StringComparison.OrdinalIgnoreCase))
        {
            helpTask = GetSpecificHelp(chatId, messageId, subject.ToLower(), helpConfig);
        }
        else
        {
            helpTask = GetCommandsHelp(chatId, messageId, helpConfig);
        }

        return await Array(helpTask,
            messageAssistance.DeleteCommandMessage(chatId, parameters.MessageId, Command)).WhenAll();
    }

    private async Task<Unit> GetCommandsHelp(long chatId, int messageId, HelpConfig2 helpConfig)
    {
        if (!await lockRepository.TryAcquire(chatId, Command))
        {
            return await messageAssistance.CommandNotReady(chatId, messageId, Command);
        }

        var builder = new StringBuilder();
        builder.AppendLine(helpConfig.HelpTitle);
        foreach (var (command, description) in CommandDescriptions)
        {
            builder.AppendLine(MakeCommandHelpString(command, description));
        }

        return await messageAssistance.SendCommandResponse(chatId, builder.ToString(), Command);
    }

    private async Task<Unit> GetSpecificHelp(long chatId, int messageId, string subject, HelpConfig2 helpConfig)
    {
        var command = $"{Command}={subject}";
        if (!await lockRepository.TryAcquire(chatId, Command, command))
        {
            return await messageAssistance.CommandNotReady(chatId, messageId, command);
        }

        if (!Enum.TryParse(subject, true, out MemberItemType itemType))
            return await GetCommandHelp(chatId, AddCommandPrefix(subject), helpConfig);

        var maybePassiveItem = passiveItems.Value.Find(x => x.ItemType == itemType);
        return await maybePassiveItem.MatchAsync(
            Some: passiveItem => SendItemHelp(chatId, passiveItem.ItemType, passiveItem.Description, helpConfig),
            None: () => GetActiveItemHelp(chatId, AddCommandPrefix(subject), itemType, helpConfig));
    }

    private Task<Unit> GetActiveItemHelp(long chatId, string command, MemberItemType itemType, HelpConfig2 helpConfig)
    {
        command = itemType switch
        {
            MemberItemType.Box => OpenBoxCommandHandler.CommandKey,
            _ => command
        };
        return CommandDescriptions.Find(command).Match(
            Some: description =>
                SendItemHelp(chatId, itemType, MakeCommandHelpString(command, description), helpConfig),
            None: () => SendHelpNotFound(chatId, command, helpConfig));
    }

    private Task<Unit> GetCommandHelp(long chatId, string command, HelpConfig2 helpConfig) =>
        CommandDescriptions.Find(command).Match(
            Some: description =>
                SendCommandHelp(chatId, command, MakeCommandHelpString(command, description), helpConfig),
            None: () => SendHelpNotFound(chatId, command, helpConfig));

    private Task<Unit> SendCommandHelp(long chatId, string command, string description, HelpConfig2 helpConfig)
    {
        var message = string.Format(helpConfig.CommandHelpTemplate, command, description);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendItemHelp(long chatId, MemberItemType item, string description, HelpConfig2 helpConfig)
    {
        var message = string.Format(helpConfig.ItemHelpTemplate, item, description);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendHelpNotFound(long chatId, string subject, HelpConfig2 helpConfig)
    {
        Log.Information("Help not found for: {0}", subject);
        return messageAssistance.SendCommandResponse(chatId, helpConfig.FailedMessage, Command);
    }

    private string MakeCommandHelpString(string command, string description) => $"{command} - {description}";
    private string AddCommandPrefix(string subject) => "/" + subject;
}