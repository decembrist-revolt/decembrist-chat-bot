using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand.Items;
using JasperFx.Core;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class CraftCommandHandler(MessageAssistance messageAssistance) : ICommandHandler
{
    public string Command => "/craft";
    public string Description => "craft items";
    public CommandLevel CommandLevel => CommandLevel.User;

    [GeneratedRegex(@"\s+")]
    private static partial Regex ArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var taskResult = ParseText(text.Trim()).Match(
            inputItems => HandleCraft(inputItems, chatId, telegramId),
            () => SendHelp(chatId));

        return await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private Task<Unit> SendHelp(long chatId)
    {
        throw new NotImplementedException();
    }

    private Task<Unit> HandleCraft(List<InputItem> inputItems, long chatId, long telegramId)
    {
        throw new NotImplementedException();
    }

    private Option<List<InputItem>> ParseText(string text)
    {
        var argsPosition = text.IndexOf(' ');
        return Optional(argsPosition != -1 ? text[(argsPosition + 1)..] : string.Empty)
            .Filter(arg => arg.IsNotEmpty())
            .Map(arg => ArgsRegex().Replace(arg, " ").Trim())
            .Filter(arg => arg.Length > 0)
            .Map(x => x.Split(' '))
            .Map(x => x.Map(ParseCraftInput).Somes().ToList())
            .Filter(result => result.Count > 0);
    }

    private Option<InputItem> ParseCraftInput(string input)
    {
        if (input.Contains('@'))
        {
            if (input.Split('@') is [var itemString, var quantityString] &&
                Enum.TryParse(itemString, out MemberItemType item) &&
                int.TryParse(quantityString, out var quantity))
            {
                return new InputItem(item, quantity);
            }
        }
        else if (Enum.TryParse(input, out MemberItemType item)) return new InputItem(item);

        return None;
    }
}