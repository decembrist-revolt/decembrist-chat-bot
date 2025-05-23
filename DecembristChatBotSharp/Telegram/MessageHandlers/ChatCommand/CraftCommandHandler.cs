using System.Text.RegularExpressions;
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

        var items = GetCraftItems(text.Trim());
        var resultTask = items.Count > 0 ? HandleCraft(items, chatId, telegramId) : SendHelp(chatId);

        return await Array(
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            resultTask).WhenAll();
    }

    private async Task<Unit> HandleCraft(List<ItemQuantity> inputItems, long chatId, long telegramId)
    {
        var result = await craftService.HandleCraft(inputItems, chatId, telegramId);
        result.LogCraftResult(telegramId, chatId);

        return result.CraftResult switch
        {
            CraftResult.Failed => await SendFailed(chatId),
            CraftResult.Success => await SendSuccess(chatId, result.CraftReward!),
            CraftResult.PremiumSuccess => await SendPremiumSuccess(chatId, result.CraftReward!),
            CraftResult.NoRecipe => await SendNoRecipe(chatId),
            CraftResult.NoItems => await messageAssistance.SendNoItems(chatId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private Task<Unit> SendSuccess(long chatId, ItemQuantity reward)
    {
        var message = string.Format(appConfig.CraftConfig.SuccessMessage, reward.Quantity, reward.Item);
        var expirationDate = DateTime.UtcNow.AddMinutes(appConfig.CraftConfig.SuccessExpiration);
        return messageAssistance.SendCommandResponse(chatId, message, Command, expirationDate);
    }

    private Task<Unit> SendPremiumSuccess(long chatId, ItemQuantity reward)
    {
        var craftConfig = appConfig.CraftConfig;
        var successMessage = string.Format(craftConfig.SuccessMessage, reward.Quantity, reward.Item);
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

    private List<ItemQuantity> GetCraftItems(string text)
    {
        var argsPosition = text.IndexOf(' ');
        var items = Optional(argsPosition != -1 ? text[(argsPosition + 1)..] : string.Empty)
            .Filter(arg => arg.IsNotEmpty())
            .Map(arg => ArgsRegex().Replace(arg, " ").Trim())
            .Filter(arg => arg.Length > 0)
            .Map(x => x.Split(' '))
            .IfNone([]);

        return items
            .Map(memberItemService.ParseItem)
            .Somes()
            .ToList();
    }
}