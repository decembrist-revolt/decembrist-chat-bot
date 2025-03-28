using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class RestrictCommandHandler(
    BotClient botClient,
    RestrictRepository restrictRepository,
    AdminUserRepository adminRepository,
    CancellationTokenSource cancelToken,
    AppConfig appConfig,
    MessageAssistance messageAssistance
) : ICommandHandler
{
    private readonly Regex _regex = new(@"\b(link)\b", RegexOptions.IgnoreCase);

    public string Command => "/restrict";
    public string Description => "Restrict user in reply";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        if (text != Command && !text.StartsWith(Command + " ")) return unit;
        if (!await adminRepository.IsAdmin(new(telegramId, chatId)))
        {
            return await messageAssistance.SendAdminOnlyMessage(chatId, telegramId);
        }

        var replyUserId = parameters.ReplyToTelegramId;
        if (replyUserId.IsNone)
        {
            Log.Warning("Reply user for {0} not set in chat {1}", Command, chatId);
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
        }

        var receiverId = replyUserId.ValueUnsafe();
        if (telegramId == receiverId)
        {
            Log.Warning("Self-targeting {0} in chat {1}", Command, chatId);
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
        }

        var compositeId = new CompositeId(receiverId, chatId);
        if (text.Contains("clear", StringComparison.OrdinalIgnoreCase))
        {
            if (await DeleteRestrict(compositeId, messageId))
                Log.Information("Clear restrict for {0} in chat {1} by {2}", receiverId, chatId, telegramId);
            else
                Log.Error("Restrict not cleared for {0} in chat {1} by {2}", receiverId, chatId, telegramId);
            return unit;
        }

        var restrictType = ParseRestrictType(text);
        if (restrictType == RestrictType.None)
        {
            Log.Warning("Command parameters are missing for {0} in chat {1}", Command, chatId);
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
        }

        if (await AddRestrict(new RestrictMember(compositeId, restrictType), messageId))
            Log.Information(
                "Added restrict for {0} in chat {1} by {2} with type {3}", receiverId, chatId, telegramId,
                restrictType);
        else
            Log.Error("Restrict not added for {0} in chat {1} by {2}", receiverId, chatId, telegramId);
        return unit;
    }

    private RestrictType ParseRestrictType(string input)
    {
        var result = RestrictType.None;
        var matches = _regex.Matches(input);
        foreach (Match match in matches)
        {
            if (!Enum.TryParse(match.Value, true, out RestrictType restrictType)) continue;
            if (restrictType == RestrictType.None) continue;
            result |= restrictType;
        }

        return result;
    }

    private async Task<bool> DeleteRestrict(CompositeId id, int messageId)
    {
        var (telegramId, chatId) = id;
        var sendClearMessageTask = botClient.GetChatMember(chatId, telegramId, cancelToken.Token)
            .ToTryAsync()
            .Map(member => member.GetUsername())
            .Match(async username => await SendRestrictClearMessage(chatId, username),
                ex =>
                {
                    Log.Error(ex, "Failed to get chat member in chat {0} with telegramId {1}", chatId, telegramId);
                    return Task.FromResult(unit);
                });

        await Array(
            sendClearMessageTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)
        ).WhenAll();

        return await restrictRepository.DeleteRestrictMember(id);
    }

    private async Task<bool> AddRestrict(RestrictMember member, int messageId)
    {
        var (telegramId, chatId) = member.Id;
        var sendRestrictMessageTask = botClient
            .GetUsername(chatId, telegramId, cancelToken.Token)
            .IfSomeAsync(username => SendRestrictMessage(chatId, telegramId, username, member.RestrictType));
        var addRestrictTask = restrictRepository.AddRestrict(member);
        await Array(
            addRestrictTask.UnitTask(),
            sendRestrictMessageTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)
        ).WhenAll();
        return await addRestrictTask;
    }

    private async Task<Unit> SendRestrictClearMessage(long chatId, string username)
    {
        var message = string.Format(appConfig.RestrictConfig.RestrictClearMessage, username);
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent restrict clear message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send restrict clear message to chat {0}", chatId), cancelToken.Token);
    }

    private async Task<Unit> SendRestrictMessage(
        long chatId, long telegramId, string username, RestrictType restrictType)
    {
        var message = string.Format(appConfig.RestrictConfig.RestrictMessage, username, restrictType);
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("{0}-message sent from {1} in chat {2} with type {3}",
                Command, telegramId, chatId, restrictType),
            ex => Log.Error(ex, "Failed to send {command} message from {0} in chat {1} with type {2}",
                Command, telegramId, chatId, restrictType),
            cancelToken.Token);
    }
}