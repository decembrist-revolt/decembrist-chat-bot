using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Service;
using JasperFx.Core;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class GiveItemCommandHandler(
    AppConfig appConfig,
    BotClient botClient,
    MemberItemService memberItemService,
    UniqueItemService uniqueItemService,
    GiveService giveService,
    MessageAssistance messageAssistance,
    ChatConfigService chatConfigService,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public string Command => "/give";

    public string Description =>
        appConfig.CommandAssistanceConfig.CommandDescriptions.GetValueOrDefault(Command,
            $"Give item to replied member, options: {_itemOptions}");

    public CommandLevel CommandLevel => CommandLevel.User;

    private readonly string _itemOptions =
        string.Join(", ", Enum.GetValues<MemberItemType>().Map(type => type.ToString()));


    [GeneratedRegex(@"\s+")]
    private static partial Regex ArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;
        var maybeGiveConfig = await chatConfigService.GetConfig(chatId, config => config.GiveConfig);
        if (!maybeGiveConfig.TryGetSome(out var giveConfig))
        {
            await messageAssistance.SendNotConfigured(chatId, messageId, Command);
            return chatConfigService.LogNonExistConfig(unit, nameof(GiveConfig), Command);
        }
        var maybeItemConfig = await chatConfigService.GetConfig(chatId, config => config.ItemConfig);
        if (!maybeItemConfig.TryGetSome(out var itemConfig))
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);

        var resultTask = parameters.ReplyToTelegramId.MatchAsync(
            None: () => SendReceiverNotSet(chatId, giveConfig),
            Some: receiverId => ParseText(text).MatchAsync(
                itemQuantity => HandleGive(chatId, telegramId, receiverId, itemQuantity, giveConfig, itemConfig),
                () => SendHelp(chatId, giveConfig)));

        return await Array(resultTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private Option<ItemQuantity> ParseText(string text) =>
        Optional(text.Split(' ', 2))
            .Bind(parts => parts.Length > 1 ? Some(parts[1]) : None)
            .Filter(arg => arg.IsNotEmpty())
            .Map(arg => ArgsRegex().Replace(arg, " ").Trim())
            .Filter(arg => arg.Length > 0)
            .Bind(memberItemService.ParseItem);

    private async Task<Unit> HandleGive(long chatId, long telegramId, long receiverId, ItemQuantity itemQuantity,
        GiveConfig giveConfig, ItemConfig itemConfig)
    {
        var (giveResult, isAmuletBroken) = await giveService.GiveItem(chatId, telegramId, receiverId, itemQuantity);
        giveResult.LogGiveResult(itemQuantity, telegramId, receiverId, chatId);

        return giveResult switch
        {
            GiveResult.NoItems => await messageAssistance.SendNoItems(chatId),
            GiveResult.NotExpired => await SendNotExpired(chatId, itemQuantity.Item, giveConfig),
            GiveResult.Success => await SendSuccess(telegramId, receiverId, chatId, itemQuantity, isAmuletBroken,
                giveConfig, itemConfig),
            GiveResult.AdminSuccess =>
                await SendAdminSuccess(telegramId, receiverId, chatId, itemQuantity, isAmuletBroken, giveConfig,
                    itemConfig),
            GiveResult.Self => await SendSelf(chatId, giveConfig),
            GiveResult.Failed => await SendFailed(chatId, giveConfig),
            _ => throw new ArgumentOutOfRangeException(nameof(giveResult), giveResult, null)
        };
    }

    private async Task<Unit> SendNotExpired(long chatId, MemberItemType itemType, GiveConfig giveConfig)
    {
        var maybeTime = await uniqueItemService.GetRemainingTime((chatId, itemType));
        return await maybeTime.MatchAsync(async time =>
            {
                var message = string.Format(giveConfig.GiveNotExpiredMessage, itemType, time);
                return await messageAssistance.SendCommandResponse(chatId, message, Command);
            },
            async () => await SendFailed(chatId, giveConfig)
        );
    }


    private async Task<Unit> SendSuccess(
        long telegramId, long receiverId, long chatId, ItemQuantity itemQuantity, bool isAmuletBroken,
        GiveConfig giveConfig, ItemConfig itemConfig)
    {
        var senderName = await botClient.GetUsernameOrId(telegramId, chatId, cancelToken.Token);
        var receiverName = await botClient.GetUsernameOrId(receiverId, chatId, cancelToken.Token);
        var message = string.Format(
            giveConfig.SuccessMessage, senderName, receiverName, itemQuantity.Item, itemQuantity.Quantity);
        if (isAmuletBroken) message += "\n\n" + itemConfig.AmuletBrokenMessage;
        var expireAt = DateTime.UtcNow.AddMinutes(giveConfig.ExpirationMinutes);
        return await messageAssistance.SendCommandResponse(chatId, message, Command, expireAt);
    }

    private async Task<Unit> SendAdminSuccess(
        long telegramId, long receiverId, long chatId, ItemQuantity itemQuantity, bool isAmuletBroken,
        GiveConfig giveConfig, ItemConfig itemConfig)
    {
        Log.Information("Admin: {0} give item: {1} for: {2}", telegramId, itemQuantity, receiverId);
        var receiverName = await botClient.GetUsernameOrId(receiverId, chatId, cancelToken.Token);
        var message = string.Format(
            giveConfig.AdminSuccessMessage, receiverName, itemQuantity.Item, itemQuantity.Quantity, Command);
        if (isAmuletBroken) message += "\n\n" + itemConfig.AmuletBrokenMessage;
        var expireAt = DateTime.UtcNow.AddMinutes(itemConfig.BoxMessageExpiration);
        return await messageAssistance.SendCommandResponse(chatId, message, Command, expireAt);
    }

    private Task<Unit> SendHelp(long chatId, GiveConfig giveConfig)
    {
        var message = string.Format(giveConfig.HelpMessage, Command, _itemOptions);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendReceiverNotSet(long chatId, GiveConfig giveConfig)
    {
        var message = string.Format(giveConfig.ReceiverNotSet, Command);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendFailed(long chatId, GiveConfig giveConfig) =>
        messageAssistance.SendCommandResponse(chatId, giveConfig.FailedMessage, Command);

    private Task<Unit> SendSelf(long chatId, GiveConfig giveConfig) =>
        messageAssistance.SendCommandResponse(chatId, giveConfig.SelfMessage, Command);
}