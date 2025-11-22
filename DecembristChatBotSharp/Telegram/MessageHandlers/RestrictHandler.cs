using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class RestrictHandler(
    BotClient botClient,
    RestrictRepository db,
    CancellationTokenSource cancelToken
)
{
    private static readonly Regex LinkRegex = new(
        @"([^\s<>]+\.[^\s<>]{2,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Dictionary<RestrictType, Predicate<IMessagePayload>> _handlers = new()
    {
        [RestrictType.Link] = payload => payload switch
        {
            TextPayload { IsLink: true } => true,
            TextPayload { Text: var text } => LinkRegex.IsMatch(text),
            _ => false
        }
    };

    /// <returns>True if message was deleted due to restriction</returns>
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        return await db.GetRestrictMember(new(telegramId, chatId))
            .MatchAsync(
                async member =>
                {
                    // First, apply timeout restriction if it exists
                    var timeoutApplied = await ApplyTimeoutIfNeeded(telegramId, chatId, member);
                    
                    // Then check if message should be deleted (e.g., for link restriction)
                    var messageDeleted = await CheckRestrictions(chatId, telegramId, parameters.Payload, messageId, member.RestrictType);
                    
                    // Return true if either timeout was applied OR message was deleted
                    return timeoutApplied || messageDeleted;
                },
                () => Task.FromResult(false));
    }

    private async Task<bool> ApplyTimeoutIfNeeded(long telegramId, long chatId, RestrictMember member)
    {
        if ((member.RestrictType & RestrictType.Timeout) != RestrictType.Timeout)
            return false;

        if (member.TimeoutMinutes <= 0)
        {
            Log.Warning("Timeout restriction for user {0} in chat {1} has invalid timeout minutes: {2}",
                telegramId, chatId, member.TimeoutMinutes);
            return false;
        }

        return await ApplyTimeoutRestriction(telegramId, chatId, member.TimeoutMinutes);
    }

    private async Task<bool> ApplyTimeoutRestriction(long telegramId, long chatId, int minutes)
    {
        var permissions = new ChatPermissions
        {
            CanSendMessages = false,
            CanSendAudios = false,
            CanSendDocuments = false,
            CanSendPhotos = false,
            CanSendVideos = false,
            CanSendVideoNotes = false,
            CanSendVoiceNotes = false,
            CanSendOtherMessages = false,
            CanAddWebPagePreviews = false,
        };

        var untilDate = DateTime.UtcNow.AddMinutes(minutes);

        return await botClient.RestrictChatMember(
                chatId: chatId,
                userId: telegramId,
                permissions: permissions,
                untilDate: untilDate,
                cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ =>
                {
                    Log.Information("Applied timeout restriction for user {0} in chat {1} for {2} minutes (until {3})",
                        telegramId, chatId, minutes, untilDate);
                    return true;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to apply timeout restriction for user {0} in chat {1}",
                        telegramId, chatId);
                    return false;
                });
    }

    /// <returns>True if message was deleted due to restriction</returns>
    private async Task<bool> CheckRestrictions(
        long chatId, long telegramId, IMessagePayload payload, int messageId, RestrictType restrictType)
    {
        if (!IsRestricted(payload, restrictType)) return false;

        await botClient.DeleteMessageAndLog(chatId, messageId,
            () => Log.Information("Deleted restrict message in chat {0}, user {1}", chatId, telegramId),
            ex => Log.Error(ex, "Failed to delete restrict message in chat {0}, user {1}", chatId, telegramId),
            cancelToken.Token);

        return true;
    }

    private bool IsRestricted(IMessagePayload payload, RestrictType restrictType)
    {
        if (restrictType == RestrictType.None) return false;

        var result = false;
        foreach (var (flag, handler) in _handlers)
        {
            if ((restrictType & flag) != flag) continue;
            result |= handler(payload);
            if (result) return true;
        }

        return false;
    }
}