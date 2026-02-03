using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class RestrictCommandHandler(
    BotClient botClient,
    RestrictRepository restrictRepository,
    CancellationTokenSource cancelToken,
    AppConfig appConfig,
    MessageAssistance messageAssistance,
    ChatConfigService chatConfigService
) : ICommandHandler
{
    public string Command => "/restrict";

    public string Description =>
        appConfig.CommandAssistanceConfig.CommandDescriptions.GetValueOrDefault(Command, "Restrict user in reply");

    public CommandLevel CommandLevel => CommandLevel.Admin;

    [GeneratedRegex(@"\b(link|timeout)\b(?:\s+(\d+))?", RegexOptions.IgnoreCase)]
    private static partial Regex ArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;
        var maybeRestrictConfig = chatConfigService.GetConfig(parameters.ChatConfig, config => config.RestrictConfig);
        if (!maybeRestrictConfig.TryGetSome(out var restrictConfig))
        {
            await messageAssistance.SendNotConfigured(chatId, messageId, Command);
            return chatConfigService.LogNonExistConfig(unit, nameof(RestrictConfig), Command);
        }

        var taskResult = parameters.ReplyToTelegramId.Match(
            async receiverId => await HandleRestrict(text, receiverId, chatId, telegramId, restrictConfig),
            () =>
            {
                Log.Warning("Reply user for {0} not set in chat {1}", Command, chatId);
                return Task.FromResult(unit);
            });

        return await Array(taskResult,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private async Task<Unit> HandleRestrict(
        string text, long receiverId, long chatId, long telegramId, RestrictConfig restrictConfig)
    {
        if (telegramId == receiverId)
        {
            Log.Warning("Self-targeting {0} in chat {1}", Command, chatId);
            return unit;
        }

        var compositeId = new CompositeId(receiverId, chatId);
        var isClear = text.Contains(ChatCommandHandler.DeleteSubcommand, StringComparison.OrdinalIgnoreCase);

        if (isClear)
        {
            // Check if specific restriction type is mentioned after 'clear'
            var clearRestriction = ParseRestrictType(text);
            if (clearRestriction.IsSome)
            {
                var (restrictTypeToRemove, _) = clearRestriction.Match(r => r, () => (RestrictType.None, 0));
                return await RemoveSpecificRestrict(compositeId, telegramId, restrictTypeToRemove, restrictConfig);
            }

            // No specific type - clear all restrictions
            return await DeleteRestrictAndLog(compositeId, telegramId, restrictConfig);
        }

        return await ParseRestrictType(text).Match(
            async restrictInfo =>
            {
                var (newRestrictType, newTimeoutMinutes) = restrictInfo;

                // Get existing restrictions to merge with new ones
                var existingMember = await restrictRepository.GetRestrictMember(compositeId);
                var finalRestrictType = existingMember.Match(
                    existing => existing.RestrictType | newRestrictType, // Merge flags
                    () => newRestrictType);

                // For timeout minutes, use the new value if specified, otherwise keep existing
                var finalTimeoutMinutes = newTimeoutMinutes > 0
                    ? newTimeoutMinutes
                    : existingMember.Match(existing => existing.TimeoutMinutes, () => 0);

                return await AddRestrictAndLog(
                    new RestrictMember(compositeId, finalRestrictType, finalTimeoutMinutes), telegramId,
                    restrictConfig);
            },
            () =>
            {
                Log.Warning("Command parameters are missing for {0} in chat {1}", Command, chatId);
                return Task.FromResult(unit);
            });
    }

    private async Task<Unit> RemoveSpecificRestrict(CompositeId id, long adminId, RestrictType restrictTypeToRemove,
        RestrictConfig restrictConfig)
    {
        var (telegramId, chatId) = id;
        var username = await botClient.GetUsername(chatId, telegramId, cancelToken.Token)
            .ToAsync()
            .IfNone(telegramId.ToString);

        var existingMember = await restrictRepository.GetRestrictMember(id);

        return await existingMember.MatchAsync(
            async existing =>
            {
                // Remove the specific restriction flag(s)
                var newRestrictType = existing.RestrictType & ~restrictTypeToRemove;

                if (newRestrictType == RestrictType.None)
                {
                    // No restrictions left, delete the record
                    await restrictRepository.DeleteRestrictMember(id);
                    await RestoreUserPermissions(telegramId, chatId);
                    Log.Information("Removed all restrictions for {0} in chat {1} by {2}", telegramId, chatId, adminId);
                    return await SendRestrictClearMessage(chatId, username, restrictConfig);
                }

                // Update with remaining restrictions
                var timeoutMinutes = (newRestrictType & RestrictType.Timeout) == RestrictType.Timeout
                    ? existing.TimeoutMinutes
                    : 0;

                await restrictRepository.AddRestrict(new RestrictMember(id, newRestrictType, timeoutMinutes));

                // If timeout was removed, restore permissions immediately
                if ((restrictTypeToRemove & RestrictType.Timeout) == RestrictType.Timeout)
                {
                    await RestoreUserPermissions(telegramId, chatId);
                }

                Log.Information("Removed restriction {0} for {1} in chat {2} by {3}",
                    restrictTypeToRemove, telegramId, chatId, adminId);

                var message = string.Format("✅ Ограничение {0} с пользователя {1} снято",
                    GetRestrictionDescription(restrictTypeToRemove, existing.TimeoutMinutes, restrictConfig), username);
                return await botClient.SendMessageAndLog(chatId, message,
                    _ => Log.Information("Sent partial restrict clear message to chat {0}", chatId),
                    ex => Log.Error(ex, "Failed to send message to chat {0}", chatId),
                    cancelToken.Token);
            },
            () =>
            {
                Log.Warning("No restrictions found for {0} in chat {1}", telegramId, chatId);
                return Task.FromResult(unit);
            });
    }

    private string GetRestrictionDescription(RestrictType restrictType, int timeoutMinutes,
        RestrictConfig restrictConfig)
    {
        var restrictions = new List<string>();
        if ((restrictType & RestrictType.Link) == RestrictType.Link)
            restrictions.Add(restrictConfig.LinkShortName);
        if ((restrictType & RestrictType.Timeout) == RestrictType.Timeout)
            restrictions.Add(string.Format(restrictConfig.TimeoutShortName, timeoutMinutes));

        return string.Join(", ", restrictions);
    }

    private Option<(RestrictType, int)> ParseRestrictType(string input)
    {
        var result = RestrictType.None;
        var timeoutMinutes = 0;
        var matches = ArgsRegex().Matches(input);

        foreach (Match match in matches)
        {
            if (!Enum.TryParse(match.Groups[1].Value, true, out RestrictType restrictType)) continue;
            if (restrictType == RestrictType.None) continue;

            result |= restrictType;

            // If it's a timeout restriction, try to parse the minutes
            if (restrictType == RestrictType.Timeout && match.Groups.Count > 2 && match.Groups[2].Success)
            {
                if (int.TryParse(match.Groups[2].Value, out var minutes) && minutes > 0)
                {
                    timeoutMinutes = minutes;
                }
            }
        }

        return result == RestrictType.None
            ? None
            : (result, timeoutMinutes);
    }

    private async Task<Unit> DeleteRestrictAndLog(CompositeId id, long adminId, RestrictConfig restrictConfig)
    {
        var (telegramId, chatId) = id;
        var username = await botClient.GetUsername(chatId, telegramId, cancelToken.Token)
            .ToAsync()
            .IfNone(telegramId.ToString);

        var isDelete = await restrictRepository.DeleteRestrictMember(id);
        LogAssistant.LogDeleteResult(isDelete, adminId, chatId, telegramId, Command);

        // Restore permissions immediately
        await RestoreUserPermissions(telegramId, chatId);

        if (isDelete) return await SendRestrictClearMessage(chatId, username, restrictConfig);
        return unit;
    }

    private async Task<Unit> RestoreUserPermissions(long telegramId, long chatId)
    {
        var permissions = new ChatPermissions
        {
            CanSendMessages = true,
            CanSendAudios = true,
            CanSendDocuments = true,
            CanSendPhotos = true,
            CanSendVideos = true,
            CanSendVideoNotes = true,
            CanSendVoiceNotes = true,
            CanSendOtherMessages = true,
            CanAddWebPagePreviews = true,
            CanInviteUsers = true,
        };

        return await botClient.RestrictChatMember(
                chatId: chatId,
                userId: telegramId,
                permissions: permissions,
                cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ => Log.Information("Restored permissions for user {0} in chat {1}", telegramId, chatId),
                ex => Log.Error(ex, "Failed to restore permissions for user {0} in chat {1}", telegramId, chatId)
            );
    }

    private async Task<Unit> AddRestrictAndLog(RestrictMember member, long adminId, RestrictConfig restrictConfig)
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
            return await SendRestrictMessage(chatId, telegramId, username, member.RestrictType, member.TimeoutMinutes,
                restrictConfig);
        }

        Log.Error("Restrict not added for {0} in chat {1} by {2}", telegramId, chatId, adminId);
        return unit;
    }

    private async Task<Unit> SendRestrictClearMessage(long chatId, string username, RestrictConfig restrictConfig)
    {
        var message = string.Format(restrictConfig.RestrictClearMessage, username);
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent restrict clear message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send restrict clear message to chat {0}", chatId), cancelToken.Token);
    }

    private async Task<Unit> SendRestrictMessage(
        long chatId, long telegramId, string username, RestrictType restrictType, int timeoutMinutes, 
        RestrictConfig restrictConfig)
    {
        var message = GetRestrictMessage(username, restrictType, timeoutMinutes, restrictConfig);
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("{0}-message sent from {1} in chat {2} with type {3}",
                Command, telegramId, chatId, restrictType),
            ex => Log.Error(ex, "Failed to send {0} message from {1} in chat {2} with type {3}",
                Command, telegramId, chatId, restrictType),
            cancelToken.Token);
    }

    private string GetRestrictMessage(
        string username, RestrictType restrictType, int timeoutMinutes, RestrictConfig restrictConfig)
    {
        // Check if multiple flags are set
        var flagCount = 0;
        if ((restrictType & RestrictType.Link) == RestrictType.Link) flagCount++;
        if ((restrictType & RestrictType.Timeout) == RestrictType.Timeout) flagCount++;

        if (flagCount > 1)
        {
            // Multiple restrictions
            var restrictions = new List<string>();
            if ((restrictType & RestrictType.Link) == RestrictType.Link)
                restrictions.Add(restrictConfig.LinkShortName);
            if ((restrictType & RestrictType.Timeout) == RestrictType.Timeout)
                restrictions.Add(string.Format(restrictConfig.TimeoutShortName, timeoutMinutes));

            return string.Format(restrictConfig.CombinedRestrictMessage,
                username, string.Join(", ", restrictions));
        }

        // Single restriction
        if ((restrictType & RestrictType.Link) == RestrictType.Link)
        {
            return string.Format(restrictConfig.LinkRestrictMessage, username);
        }

        if ((restrictType & RestrictType.Timeout) == RestrictType.Timeout)
        {
            return string.Format(restrictConfig.TimeoutRestrictMessage, username, timeoutMinutes);
        }

        // Fallback
        return string.Format(restrictConfig.CombinedRestrictMessage, username, restrictType);
    }
}