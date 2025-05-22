using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Service;
using JasperFx.Core;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class CraftCommandHandler(
    MemberItemService memberItemService,
    MessageAssistance messageAssistance,
    AppConfig appConfig,
    CraftService craftService) : ICommandHandler
{
    public string Command => "/craft";
    public string Description => "Craft items";
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

    private async Task<Unit> HandleCraft(List<InputItem> inputItems, long chatId, long telegramId)
    {
        var result = await craftService.HandleCraft(inputItems, chatId, telegramId);
        result.LogCraftResult(telegramId, chatId);

        return result.CraftResult switch
        {
            CraftResult.Failed => await SendFailed(chatId),
            CraftResult.Success => await SendSuccess(chatId, result.CraftReward),
            CraftResult.PremiumSuccess => await SendPremiumSuccess(chatId, result.CraftReward),
            CraftResult.NoRecipe => await SendNoRecipe(chatId),
            CraftResult.NoItems => await messageAssistance.SendNoItems(chatId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private Task<Unit> SendSuccess(long chatId, (MemberItemType item, int quantity) craftReward)
    {
        var message = string.Format(appConfig.CraftConfig.SuccessMessage, craftReward.quantity, craftReward.item);
        var expirationDate = DateTime.UtcNow.AddMinutes(appConfig.CraftConfig.SuccessExpiration);
        return messageAssistance.SendCommandResponse(chatId, message, Command, expirationDate);
    }

    private Task<Unit> SendPremiumSuccess(long chatId, (MemberItemType item, int quantity) craftReward)
    {
        var craftConfig = appConfig.CraftConfig;
        var successMessage = string.Format(craftConfig.SuccessMessage, craftReward.quantity, craftReward.item);
        var message = string.Format(craftConfig.PremiumSuccessMessage, successMessage, craftConfig.PremiumBonus);
        var expirationDate = DateTime.UtcNow.AddMinutes(craftConfig.SuccessExpiration);
        return messageAssistance.SendCommandResponse(chatId, message, Command, expirationDate);
    }

    private Task<Unit> SendNoRecipe(long chatId)
    {
        var message = appConfig.CraftConfig.NoRecipeMessage;
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendHelp(long chatId)
    {
        var message = string.Format(appConfig.CraftConfig.HelpMessage, Command);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendFailed(long chatId)
    {
        var message = appConfig.CraftConfig.FailedMessage;
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Option<List<InputItem>> ParseText(string text)
    {
        var argsPosition = text.IndexOf(' ');
        return Optional(argsPosition != -1 ? text[(argsPosition + 1)..] : string.Empty)
            .Filter(arg => arg.IsNotEmpty())
            .Map(arg => ArgsRegex().Replace(arg, " ").Trim())
            .Filter(arg => arg.Length > 0)
            .Map(x => x.Split(' '))
            .Map(x => x.Map(memberItemService.ParseItem).Somes()
                .Map(itemAndQuantity => new InputItem(itemAndQuantity.Item1, itemAndQuantity.Item2)).ToList())
            .Filter(result => result.Count > 0);
    }
}