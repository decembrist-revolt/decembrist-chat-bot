using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;

[Singleton]
public class GiveawayCallbackHandler(
    AppConfig appConfig,
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
        
        if (!TryParseGiveawayData(suffix, out var item, out var quantity, out var targetAudience))
        {
            Log.Warning("Failed to parse giveaway data from suffix: {0}", suffix);
            return await SendError(queryId, chatId);
        }

        var participantId = new GiveawayParticipant.CompositeId(chatId, messageId, telegramId);
        var hasParticipated = await giveawayParticipantRepository.HasParticipated(participantId);
        
        if (hasParticipated)
        {
            return await SendAlreadyReceived(queryId, chatId);
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
            return await SendError(queryId, chatId);
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
            return await SendError(queryId, chatId);
        }

        if (!await session.TryCommit(cancelToken.Token))
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to commit giveaway participation for user {0} in chat {1}", telegramId, chatId);
            return await SendError(queryId, chatId);
        }

        Log.Information("User {0} received giveaway item {1}x{2} in chat {3}", telegramId, item, quantity, chatId);
        
        // Send callback confirmation
        await messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix, 
            string.Format(appConfig.GiveawayConfig.SuccessMessage, quantity, item), showAlert: false);
        
        // Send public message to chat
        return await SendPublicSuccess(chatId, telegramId, item, quantity);
    }

    private async Task<Unit> SendPublicSuccess(long chatId, long telegramId, MemberItemType item, int quantity)
    {
        var username = await botClient.GetUsernameOrId(telegramId, chatId, cancelToken.Token);
        var message = string.Format(appConfig.GiveawayConfig.PublicSuccessMessage, username, item, quantity);
        var expireAt = DateTime.UtcNow.AddMinutes(appConfig.ItemConfig.BoxMessageExpiration);
        return await messageAssistance.SendCommandResponse(chatId, message, Prefix, expireAt);
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
            if (!int.TryParse(parts[1], out quantity)) return false;
            if (!Enum.TryParse(parts[2], out targetAudience)) return false;

            return true;
        }
        catch
        {
            return false;
        }
    }
    
    private Task<Unit> SendAlreadyReceived(string queryId, long chatId)
    {
        var message = appConfig.GiveawayConfig.AlreadyReceivedMessage;
        return messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix, message, showAlert: true);
    }

    private Task<Unit> SendNoPremium(string queryId, long chatId)
    {
        var message = appConfig.CommandConfig.PremiumConfig.NotPremiumMessage;
        return messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix, message, showAlert: true);
    }

    private Task<Unit> SendError(string queryId, long chatId)
    {
        var message = appConfig.GiveawayConfig.ErrorMessage;
        return messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix, message, showAlert: true);
    }
}

