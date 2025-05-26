using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
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
    GiveService giveService,
    MessageAssistance messageAssistance,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public string Command => "/give";
    public string Description => $"Give item to replied member, options: {_itemOptions}";
    public CommandLevel CommandLevel => CommandLevel.User;

    private readonly string _itemOptions =
        string.Join(", ", Enum.GetValues<MemberItemType>().Map(type => type.ToString()));

    [GeneratedRegex(@"\s+")]
    private static partial Regex ArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var resultTask = parameters.ReplyToTelegramId.MatchAsync(
            None: () => SendReceiverNotSet(chatId),
            Some: receiverId => ParseText(text).MatchAsync(
                itemQuantity => HandleGive(chatId, telegramId, receiverId, itemQuantity),
                () => SendHelp(chatId)));

        return await Array(resultTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private Option<ItemQuantity> ParseText(string text)
    {
        var argsPosition = text.IndexOf(' ');
        var maybeItem = Optional(argsPosition != -1 ? text[(argsPosition + 1)..] : string.Empty)
            .Filter(arg => arg.IsNotEmpty())
            .Map(arg => ArgsRegex().Replace(arg, " ").Trim())
            .Filter(arg => arg.Length > 0);
        return maybeItem.Bind(memberItemService.ParseItem);
    }

    private async Task<Unit> HandleGive(long chatId, long telegramId, long receiverId, ItemQuantity itemQuantity)
    {
        var (giveResult, isAmuletBroken) = await giveService.GiveItem(chatId, telegramId, receiverId, itemQuantity);
        giveResult.LogGiveResult(itemQuantity, telegramId, receiverId, chatId);

        return giveResult switch
        {
            GiveResult.NoItems => await messageAssistance.SendNoItems(chatId),
            GiveResult.Success => await SendSuccess(telegramId, receiverId, chatId, itemQuantity, isAmuletBroken),
            GiveResult.AdminSuccess =>
                await SendAdminSuccess(telegramId, receiverId, chatId, itemQuantity, isAmuletBroken),
            GiveResult.Failed => await SendFailed(chatId),
            _ => throw new ArgumentOutOfRangeException(nameof(giveResult), giveResult, null)
        };
    }

    private async Task<Unit> SendSuccess(
        long telegramId, long receiverId, long chatId, ItemQuantity itemQuantity, bool isAmuletBroken)
    {
        var senderName = await botClient.GetUsernameOrId(telegramId, chatId, cancelToken.Token);
        var receiverName = await botClient.GetUsernameOrId(receiverId, chatId, cancelToken.Token);
        var message = string.Format(
            appConfig.GiveConfig.SuccessMessage, senderName, receiverName, itemQuantity.Item, itemQuantity.Quantity);
        if (isAmuletBroken) message += "\n\n" + appConfig.ItemConfig.AmuletBrokenMessage;
        var expireAt = DateTime.UtcNow.AddMinutes(appConfig.GiveConfig.ExpirationMinutes);
        return await messageAssistance.SendCommandResponse(chatId, message, Command, expireAt);
    }

    private async Task<Unit> SendAdminSuccess(
        long telegramId, long receiverId, long chatId, ItemQuantity itemQuantity, bool isAmuletBroken)
    {
        Log.Information("Admin: {0} give item: {1} for: {2}", telegramId, itemQuantity, receiverId);
        var receiverName = await botClient.GetUsernameOrId(receiverId, chatId, cancelToken.Token);
        var message = string.Format(
            appConfig.GiveConfig.AdminSuccessMessage, receiverName, itemQuantity.Item, itemQuantity.Quantity, Command);
        if (isAmuletBroken) message += "\n\n" + appConfig.ItemConfig.AmuletBrokenMessage;
        var expireAt = DateTime.UtcNow.AddMinutes(appConfig.ItemConfig.BoxMessageExpiration);
        return await messageAssistance.SendCommandResponse(chatId, message, Command, expireAt);
    }

    private Task<Unit> SendHelp(long chatId)
    {
        var message = string.Format(appConfig.GiveConfig.HelpMessage, Command, _itemOptions);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendReceiverNotSet(long chatId)
    {
        var message = string.Format(appConfig.GiveConfig.ReceiverNotSet, Command);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendFailed(long chatId)
    {
        var message = appConfig.GiveConfig.FailedMessage;
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }
}