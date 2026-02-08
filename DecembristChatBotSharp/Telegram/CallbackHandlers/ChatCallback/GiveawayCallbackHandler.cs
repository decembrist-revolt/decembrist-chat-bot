using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;

[Singleton]
public class GiveawayCallbackHandler(
    AppConfig appConfig,
    ChatConfigService chatConfigService,
    GiveawayParticipantRepository giveawayParticipantRepository,
    MemberItemRepository memberItemRepository,
    PremiumMemberService premiumMemberService,
    MessageAssistance messageAssistance,
    HistoryLogRepository historyLogRepository,
    BotClient botClient,
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IChatCallbackHandler
{
    public const string PrefixKey = "Giveaway";

    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, chatId, telegramId, messageId, queryId, _) = queryParameters;

        var maybeGiveawayConfig = await chatConfigService.GetConfig(chatId, config => config.GiveawayConfig);
        if (!maybeGiveawayConfig.TryGetSome(out var giveawayConfig))
        {
            return chatConfigService.LogNonExistConfig(unit, nameof(GiveawayConfig));
        }

        var maybeItemConfig = await chatConfigService.GetConfig(chatId, config => config.ItemConfig);
        if (!maybeItemConfig.TryGetSome(out var itemConfig))
        {
            return chatConfigService.LogNonExistConfig(unit, nameof(ItemConfig));
        }

        if (!TryParseGiveawayData(suffix, out var item, out var quantity, out var targetAudience))
        {
            Log.Warning("Failed to parse giveaway data from suffix: {0}", suffix);
            return await SendError(queryId, chatId, giveawayConfig);
        }

        var participantId = new GiveawayParticipant.CompositeId(chatId, messageId, telegramId);
        var hasParticipated = await giveawayParticipantRepository.HasParticipated(participantId);

        if (hasParticipated)
        {
            return await SendAlreadyReceived(queryId, chatId, giveawayConfig);
        }

        // Check premium requirement
        if (targetAudience == GiveawayTargetAudience.PremiumOnly)
        {
            var isPremium = await premiumMemberService.IsPremium(telegramId, chatId);
            if (!isPremium)
            {
                return await SendNoPremium(queryId, chatId);
            }
        }

        // Give item using session
        using var session = await db.OpenSession();
        session.StartTransaction();

        var success = await memberItemRepository.AddMemberItem(chatId, telegramId, item, session, quantity);
        if (!success)
        {
            await session.TryAbort(cancelToken.Token);
            return await SendError(queryId, chatId, giveawayConfig);
        }

        // Log the item
        await historyLogRepository.LogItem(chatId, telegramId, item, quantity,
            MemberItemSourceType.Giveaway, session);

        // Add participant record
        var expireAt = DateTime.UtcNow.AddHours(25); // Slightly more than 24h for safety
        var participant = new GiveawayParticipant(participantId, DateTime.UtcNow, expireAt);
        var participantAdded = await giveawayParticipantRepository.AddParticipant(participant, session);

        if (!participantAdded)
        {
            await session.TryAbort(cancelToken.Token);
            return await SendError(queryId, chatId, giveawayConfig);
        }

        if (!await session.TryCommit(cancelToken.Token))
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to commit giveaway participation for user {0} in chat {1}", telegramId, chatId);
            return await SendError(queryId, chatId, giveawayConfig);
        }

        Log.Information("User {0} received giveaway item {1}x{2} in chat {3}", telegramId, item, quantity, chatId);

        // Send callback confirmation
        await messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix,
            string.Format(giveawayConfig.SuccessMessage, quantity, item), showAlert: false);

        // Send public message to chat
        return await SendPublicSuccess(chatId, telegramId, item, quantity, giveawayConfig, itemConfig);
    }

    private async Task<Unit> SendPublicSuccess(long chatId, long telegramId, MemberItemType item, int quantity,
        GiveawayConfig giveawayConfig, ItemConfig itemConfig)
    {
        var username = await botClient.GetUsernameOrId(telegramId, chatId, cancelToken.Token);
        var message = string.Format(giveawayConfig.PublicSuccessMessage, username, item, quantity);
        var expireAt = DateTime.UtcNow.AddMinutes(itemConfig.BoxMessageExpiration);
        return await messageAssistance.SendMessageExpired(chatId, message, Prefix, expireAt);
    }

    private bool TryParseGiveawayData(string suffix, out MemberItemType item, out int quantity,
        out GiveawayTargetAudience targetAudience)
    {
        item = default;
        quantity = 0;
        targetAudience = GiveawayTargetAudience.PremiumOnly;

        try
        {
            // Format: "ItemType_Quantity_TargetAudience"
            var parts = suffix.Split('_');
            if (parts.Length < 3) return false;

            if (!Enum.TryParse(parts[0], out item)) return false;

            return int.TryParse(parts[1], out quantity) && Enum.TryParse(parts[2], out targetAudience);
        }
        catch
        {
            return false;
        }
    }

    private Task<Unit> SendAlreadyReceived(string queryId, long chatId, GiveawayConfig giveawayConfig)
    {
        var message = giveawayConfig.AlreadyReceivedMessage;
        return messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix, message, showAlert: true);
    }

    private async Task<Unit> SendNoPremium(string queryId, long chatId)
    {
        var message = appConfig.CommandAssistanceConfig.PremiumConfig.NotPremiumMessage;
        return await messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix, message, showAlert: true);
    }

    private Task<Unit> SendError(string queryId, long chatId, GiveawayConfig giveawayConfig)
    {
        var message = giveawayConfig.ErrorMessage;
        return messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix, message, showAlert: true);
    }
}