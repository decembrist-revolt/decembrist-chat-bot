using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Service;
using JasperFx.Core;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class CraftCommandHandler(
    AppConfig appConfig,
    MemberItemService memberItemService,
    MessageAssistance messageAssistance,
    CraftService craftService,
    ChatConfigService chatConfigService) : ICommandHandler
{
    public string Command => "/craft";
    public string Description => appConfig.CommandAssistanceConfig.CommandDescriptions.GetValueOrDefault(Command, "Craft items");
    public CommandLevel CommandLevel => CommandLevel.User;

    [GeneratedRegex(@"\s+")]
    private static partial Regex ArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;
        var maybeCraftConfig = await chatConfigService.GetConfig(chatId, config => config.CraftConfig);
        if (!maybeCraftConfig.TryGetSome(out var craftConfig))
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);

        var items = GetCraftItems(text.Trim());
        var resultTask = items.Count > 0
            ? HandleCraft(items, chatId, telegramId, craftConfig)
            : SendHelp(chatId, craftConfig);

        return await Array(
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            resultTask).WhenAll();
    }

    private async Task<Unit> HandleCraft(List<ItemQuantity> inputItems, long chatId, long telegramId,
        CraftConfig craftConfig)
    {
        var result = await craftService.HandleCraft(inputItems, chatId, telegramId);
        result.LogCraftResult(telegramId, chatId);

        return result.CraftResult switch
        {
            CraftResult.Failed => await SendFailed(chatId, craftConfig),
            CraftResult.Success => await SendSuccess(chatId, result.CraftReward!, craftConfig),
            CraftResult.PremiumSuccess => await SendPremiumSuccess(chatId, result.CraftReward!, craftConfig),
            CraftResult.NoRecipe => await SendNoRecipe(chatId, craftConfig),
            CraftResult.NoItems => await messageAssistance.SendNoItems(chatId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private Task<Unit> SendSuccess(long chatId, ItemQuantity reward, CraftConfig craftConfig)
    {
        var message = string.Format(craftConfig.SuccessMessage, reward.Quantity, reward.Item);
        var expirationDate = DateTime.UtcNow.AddMinutes(craftConfig.SuccessExpiration);
        return messageAssistance.SendCommandResponse(chatId, message, Command, expirationDate);
    }

    private Task<Unit> SendPremiumSuccess(long chatId, ItemQuantity reward, CraftConfig craftConfig)
    {
        var successMessage = string.Format(craftConfig.SuccessMessage, reward.Quantity, reward.Item);
        var message = string.Format(craftConfig.PremiumSuccessMessage, successMessage, craftConfig.PremiumBonus);
        var expirationDate = DateTime.UtcNow.AddMinutes(craftConfig.SuccessExpiration);
        return messageAssistance.SendCommandResponse(chatId, message, Command, expirationDate);
    }

    private Task<Unit> SendNoRecipe(long chatId, CraftConfig craftConfig)
    {
        var message = craftConfig.NoRecipeMessage;
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendHelp(long chatId, CraftConfig craftConfig)
    {
        var message = string.Format(craftConfig.HelpMessage, Command);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendFailed(long chatId, CraftConfig craftConfig)
    {
        var message = craftConfig.FailedMessage;
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