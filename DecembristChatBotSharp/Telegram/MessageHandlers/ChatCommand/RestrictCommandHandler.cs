using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;

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
    public CommandLevel CommandLevel => CommandLevel.Admin;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var isAdmin = await adminRepository.IsAdmin((telegramId, chatId));
        var taskResult = !isAdmin
            ? messageAssistance.SendAdminOnlyMessage(chatId, telegramId)
            : parameters.ReplyToTelegramId.Match(
                async receiverId => await HandleRestrict(text, receiverId, chatId, telegramId, messageId),
                () =>
                {
                    Log.Warning("Reply user for {0} not set in chat {1}", Command, chatId);
                    return Task.FromResult(unit);
                });

        return await Array(taskResult,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private async Task<Unit> HandleRestrict(string text, long receiverId, long chatId, long telegramId, int messageId)
    {
        if (telegramId == receiverId)
        {
            Log.Warning("Self-targeting {0} in chat {1}", Command, chatId);
            return unit;
        }

        var compositeId = new CompositeId(receiverId, chatId);
        var isDelete = text.Contains(ChatCommandHandler.DeleteSubcommand, StringComparison.OrdinalIgnoreCase);
        return isDelete
            ? await DeleteRestrictAndLog(compositeId, telegramId)
            : await ParseRestrictType(text).Match(
                async restrictType =>
                    await AddRestrictAndLog(new RestrictMember(compositeId, restrictType), telegramId),
                () =>
                {
                    Log.Warning("Command parameters are missing for {0} in chat {1}", Command, chatId);
                    return Task.FromResult(unit);
                });
    }

    private Option<RestrictType> ParseRestrictType(string input)
    {
        var result = RestrictType.None;
        var matches = _regex.Matches(input);
        foreach (Match match in matches)
        {
            if (!Enum.TryParse(match.Value, true, out RestrictType restrictType)) continue;
            if (restrictType == RestrictType.None) continue;
            result |= restrictType;
        }

        return result == RestrictType.None
            ? None
            : result;
    }

    private async Task<Unit> DeleteRestrictAndLog(CompositeId id, long adminId)
    {
        var (telegramId, chatId) = id;
        var username = await botClient.GetUsername(chatId, telegramId, cancelToken.Token)
            .ToAsync()
            .IfNone(telegramId.ToString);

        if (await restrictRepository.DeleteRestrictMember(id))
        {
            Log.Information("Clear restrict for {0} in chat {1} by {2}", chatId, telegramId, adminId);
            return await SendRestrictClearMessage(chatId, username);
        }

        Log.Error("Restrict not cleared for {0} in chat {1} by {2}", chatId, telegramId, adminId);
        return unit;
    }

    private async Task<Unit> AddRestrictAndLog(RestrictMember member, long adminId)
    {
        var (telegramId, chatId) = member.Id;

        var username = await botClient.GetUsername(chatId, telegramId, cancelToken.Token)
            .ToAsync()
            .IfNone(telegramId.ToString);

        if (await restrictRepository.AddRestrict(member))
        {
            Log.Information(
                "Added restrict for {0} in chat {1} by {2} with type {3}", telegramId, chatId, adminId,
                member.RestrictType);
            return await SendRestrictMessage(chatId, telegramId, username, member.RestrictType);
        }

        Log.Error("Restrict not added for {0} in chat {1} by {2}", telegramId, chatId, adminId);
        return unit;
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
            ex => Log.Error(ex, "Failed to send {0} message from {1} in chat {2} with type {3}",
                Command, telegramId, chatId, restrictType),
            cancelToken.Token);
    }
}