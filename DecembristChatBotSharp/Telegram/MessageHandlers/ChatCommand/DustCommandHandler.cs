﻿using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Service;
using JasperFx.Core;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class DustCommandHandler(
    AppConfig appConfig,
    MessageAssistance messageAssistance,
    DustService dustService,
    ChatConfigService chatConfigService) : ICommandHandler
{
    public const string CommandKey = "/dust";
    public string Command => CommandKey;

    public string Description =>
        appConfig.CommandAssistanceConfig.CommandDescriptions.GetValueOrDefault(CommandKey,
            "Dust an item, Dust and its varieties appear from other items, Used as crafting ingredients");

    public CommandLevel CommandLevel => CommandLevel.User;

    [GeneratedRegex(@"\s+")]
    private static partial Regex ArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;
        var maybeDustConfig = await chatConfigService.GetConfig(chatId, config => config.DustConfig);
        if (!maybeDustConfig.TryGetSome(out var dustConfig))
        {
            await messageAssistance.SendNotConfigured(chatId, messageId, Command);
            return chatConfigService.LogNonExistConfig(unit, nameof(DustConfig), Command);
        }

        var taskResult = ParseText(text.Trim()).Match(
            item => HandleDust(item, chatId, telegramId, dustConfig),
            () => SendHelp(chatId, dustConfig));

        return await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private Option<MemberItemType> ParseText(string text)
    {
        var argsPosition = text.IndexOf(' ');
        return Optional(argsPosition != -1 ? text[(argsPosition + 1)..] : string.Empty)
            .Filter(arg => arg.IsNotEmpty())
            .Map(arg => ArgsRegex().Replace(arg, " ").Trim())
            .Filter(arg => arg.Length > 0)
            .Bind(x => Enum.TryParse(x, true, out MemberItemType item) ? Some(item) : None);
    }

    private async Task<Unit> HandleDust(MemberItemType item, long chatId, long telegramId, DustConfig dustConfig)
    {
        var result = await dustService.HandleDust(item, chatId, telegramId);
        result.LogDustResult(telegramId, chatId);

        return result.Result switch
        {
            DustResult.Success => await SendSuccess(chatId, result.DustReward!, item, dustConfig),
            DustResult.PremiumSuccess => await SendPremiumSuccess(chatId, result, item, dustConfig),
            DustResult.NoRecipe => await SendNoRecipe(chatId, dustConfig),
            DustResult.NoItems => await messageAssistance.SendNoItems(chatId),
            DustResult.Failed => await SendFailed(chatId, dustConfig),
            _ => throw new ArgumentOutOfRangeException(nameof(result.Result))
        };
    }

    private Task<Unit> SendPremiumSuccess(long chatId, DustOperationResult result, MemberItemType recipeItem,
        DustConfig dustConfig)
    {
        var (dustItem, dustQuantity) = result.DustReward!;
        var (premiumItem, premiumQuantity) = result.PremiumReward!;
        var successMessage = string.Format(dustConfig.SuccessMessage, recipeItem, dustQuantity, dustItem);
        var message = string.Format(dustConfig.PremiumSuccessMessage,
            successMessage, premiumQuantity, premiumItem);
        var expireAt = DateTime.UtcNow.AddMinutes(dustConfig.SuccessExpiration);
        return messageAssistance.SendCommandResponse(chatId, message, Command, expireAt);
    }

    private Task<Unit> SendSuccess(
        long chatId, ItemQuantity dustReward, MemberItemType recipeItem, DustConfig dustConfig)
    {
        var message = string.Format(
            dustConfig.SuccessMessage, recipeItem, dustReward.Quantity, dustReward.Item);
        var expireAt = DateTime.UtcNow.AddMinutes(dustConfig.SuccessExpiration);
        return messageAssistance.SendCommandResponse(chatId, message, Command, expireAt);
    }

    private Task<Unit> SendHelp(long chatId, DustConfig dustConfig)
    {
        var message = string.Format(dustConfig.HelpMessage, Command);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendNoRecipe(long chatId, DustConfig dustConfig)
    {
        var message = dustConfig.NoRecipeMessage;
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendFailed(long chatId, DustConfig dustConfig)
    {
        var message = dustConfig.FailedMessage;
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }
}