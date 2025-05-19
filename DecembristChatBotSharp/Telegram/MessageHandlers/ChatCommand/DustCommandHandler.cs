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
    AppConfig appConfig,
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
            async item => await HandleCommand(item, chatId, telegramId),
            async () => await SendHelp(chatId));

        return await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private Task<Unit> SendHelp(long chatId)
    {
        var message = appConfig.DustConfig.HelpMessage;
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendNoRecipe(long chatId)
    {
        var message = string.Format(appConfig.DustConfig.NoRecipeMessage, appConfig.DustConfig.HelpMessage);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendFailed(long chatId)
    {
        var message = appConfig.DustConfig.FailedMessage;
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> HandleCommand(MemberItemType item, long chatId, long telegramId)
    {
        var result = await dustService.HandleDust(item, chatId, telegramId);
        return result.Result switch
        {
            DustResult.Success => expr,
            DustResult.PremiumSuccess => expr,
            DustResult.NoRecipe => await SendNoRecipe(chatId),
            DustResult.NoItems => await messageAssistance.SendNoItems(chatId),
            DustResult.Failed => await SendFailed(chatId),
            _ => throw new ArgumentOutOfRangeException(nameof(result.Result))
        };
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