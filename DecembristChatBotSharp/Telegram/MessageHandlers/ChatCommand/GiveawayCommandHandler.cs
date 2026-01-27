using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static DecembristChatBotSharp.Service.CallbackService;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class GiveawayCommandHandler(
    AppConfig appConfig,
    BotClient botClient,
    MessageAssistance messageAssistance,
    AdminUserRepository adminUserRepository,
    ExpiredMessageRepository expiredMessageRepository,
    ChatConfigService chatConfigService,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public string Command => "/giveaway";

    public string Description =>
        appConfig.CommandAssistanceConfig.CommandDescriptions.GetValueOrDefault(Command,
            "Start a giveaway with prizes for users");

    public CommandLevel CommandLevel => CommandLevel.Admin;

    private readonly string _itemOptions =
        string.Join(", ", Enum.GetValues<MemberItemType>().Map(type => type.ToString()));

    [GeneratedRegex(@"^(\w+)@(\d+)(?:\s+(premium|all))?(?:\s+(\d+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex GiveawayArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var isAdmin = await adminUserRepository.IsAdmin((telegramId, chatId));
        if (!isAdmin)
        {
            return await Array(
                messageAssistance.SendAdminOnlyMessage(chatId, telegramId),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)
            ).WhenAll();
        }

        var maybeGiveawayConfig = await chatConfigService.GetConfig(chatId, config => config.GiveawayConfig);
        if (!maybeGiveawayConfig.TryGetSome(out var giveawayConfig))
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);

        var parseResult = ParseGiveawayArgs(text, giveawayConfig);
        return await parseResult.MatchAsync(
            async data => await HandleGiveaway(chatId, messageId, data, giveawayConfig),
            async () => await HandleInvalidArgs(chatId, messageId, giveawayConfig)
        );
    }

    private Option<GiveawayData> ParseGiveawayArgs(string text, GiveawayConfig giveawayConfig)
    {
        var parts = text.Split(' ', 2);
        if (parts.Length < 2) return None;

        var argsText = parts[1].Trim();
        var match = GiveawayArgsRegex().Match(argsText);

        if (!match.Success) return None;

        var itemName = match.Groups[1].Value;
        var quantityStr = match.Groups[2].Value;
        var audienceStr = match.Groups[3].Success ? match.Groups[3].Value.ToLower() : "premium";
        var durationStr = match.Groups[4].Success ? match.Groups[4].Value : null;

        if (!Enum.TryParse<MemberItemType>(itemName, true, out var item)) return None;
        if (!int.TryParse(quantityStr, out var quantity) || quantity <= 0) return None;

        var audience = audienceStr == "all"
            ? GiveawayTargetAudience.All
            : GiveawayTargetAudience.PremiumOnly;

        var durationMinutes = durationStr != null && int.TryParse(durationStr, out var minutes) && minutes > 0
            ? minutes
            : giveawayConfig.DefaultDurationMinutes;

        return Some(new GiveawayData(item, quantity, audience, durationMinutes));
    }

    private async Task<Unit> HandleGiveaway(long chatId, int commandMessageId, GiveawayData data,
        GiveawayConfig giveawayConfig)
    {
        var (item, quantity, audience, durationMinutes) = data;

        // Create callback data for the button
        var callbackSuffix = $"{item}_{quantity}_{audience}";
        var callback = GetCallback<string>("Giveaway", callbackSuffix);

        // Create inline keyboard with the button
        var button = InlineKeyboardButton.WithCallbackData(giveawayConfig.ButtonText, callback);
        var keyboard = new InlineKeyboardMarkup(button);

        // Create the message text
        var audienceText = audience == GiveawayTargetAudience.All
            ? "всем участникам"
            : "участникам с премиумом";

        var message = string.Format(
            giveawayConfig.AnnouncementMessage,
            quantity,
            item,
            audienceText,
            durationMinutes
        );

        // Send the giveaway message
        await botClient.SendMessageAndLog(
            chatId,
            message,
            ParseMode.None,
            sentMessage =>
            {
                Log.Information("Created giveaway in chat {0}: {1}x{2} for {3}, duration: {4} minutes",
                    chatId, quantity, item, audience.ToString(), durationMinutes);

                // Store the message for auto-deletion
                var expireAt = DateTime.UtcNow.AddMinutes(durationMinutes);
                expiredMessageRepository.QueueMessage(chatId, sentMessage.MessageId, expireAt);
            },
            ex => { Log.Error(ex, "Failed to send giveaway message to chat {0}", chatId); },
            cancelToken.Token,
            keyboard
        );

        // Delete the command message
        return await messageAssistance.DeleteCommandMessage(chatId, commandMessageId, Command);
    }

    private async Task<Unit> HandleInvalidArgs(long chatId, int commandMessageId, GiveawayConfig giveawayConfig)
    {
        var helpMessage = string.Format(
            giveawayConfig.HelpMessage,
            Command,
            _itemOptions
        );

        return await Array(
            messageAssistance.SendCommandResponse(chatId, helpMessage, Command),
            messageAssistance.DeleteCommandMessage(chatId, commandMessageId, Command)
        ).WhenAll();
    }

    private record GiveawayData(
        MemberItemType Item,
        int Quantity,
        GiveawayTargetAudience Audience,
        int DurationMinutes);
}