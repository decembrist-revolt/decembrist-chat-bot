using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;

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
                member => CheckRestrictions(chatId, telegramId, parameters.Payload, messageId, member.RestrictType),
                () => false);
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