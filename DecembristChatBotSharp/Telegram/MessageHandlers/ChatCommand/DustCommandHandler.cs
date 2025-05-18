using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Service;
using JasperFx.Core;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class DustCommandHandler(
    MessageAssistance messageAssistance,
    DustService dustService) : ICommandHandler
{
    public string Command => "/dust";
    public string Description => "dust items";
    public CommandLevel CommandLevel => CommandLevel.User;

    [GeneratedRegex(@"\s+")]
    private static partial Regex ArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var taskResult = ParseText(text.Trim()).Match(
            async itemText => await HandleDust(itemText, chatId, telegramId),
            async () => await SendHelp(chatId));

        return await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private Task<Unit> SendHelp(long chatId)
    {
        return messageAssistance.SendCommandResponse(chatId, "help", Command);
    }

    private Task<Unit> HandleDust(MemberItemType item, long chatId, long telegramId)
    {
        Log.Information("{0}", item);
        var message = dustService.HandleDust(item, chatId, telegramId);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Option<MemberItemType> ParseText(string text)
    {
        var argsPosition = text.IndexOf(' ');
        return Optional(argsPosition != -1 ? text[(argsPosition + 1)..] : string.Empty)
            .Filter(arg => arg.IsNotEmpty())
            .Map(arg => ArgsRegex().Replace(arg, " ").Trim())
            .Filter(arg => arg.Length > 0)
            .Map(x => Enum.TryParse(x, true, out MemberItemType item)
                ? Some(item)
                : None)
            .IfNone(None);
    }
}