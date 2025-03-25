using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class RestrictCommandHandler(
    BotClient botClient,
    RestrictRepository restrictRepository,
    AdminUserRepository adminRepository,
    CancellationTokenSource cancelToken,
    AppConfig appConfig,
    ExpiredMessageRepository expiredMessageRepository,
    MessageAssistance messageAssistance
) : ICommandHandler
{
    private readonly Regex _regex = new(@"\b(link|emoji)\b", RegexOptions.IgnoreCase);

    public string Command => "/restrict";
    public string Description => "Restrict user in reply";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        if (text != Command && !text.StartsWith(Command + " ")) return unit;
        if (!await adminRepository.IsAdmin(telegramId))
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


        var id = new RestrictMember.CompositeId(receiverId, chatId);
        if (text.Contains("clear", StringComparison.OrdinalIgnoreCase))
        {
            if (await DeleteRestrict(id, messageId))
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

        var member = new RestrictMember
        {
            Id = id,
            RestrictType = restrictType
        };

        if (await AddRestrict(member, messageId))
            Log.Information(
                "Added restrict for {0} in chat {1} by {2} with type {3}", receiverId, chatId, telegramId,
                member.RestrictType);
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
            if (Enum.TryParse(match.Value, true, out RestrictType restrictType))
            {
                if (restrictType == RestrictType.None) continue;
                result |= restrictType;
            }
        }

        return result;
    }

    private async Task<bool> DeleteRestrict(RestrictMember.CompositeId id, int messageId)
    {
        var (telegramId, chatId) = id;
        var sendRestrictMessageTask = botClient.GetChatMember(chatId, telegramId, cancelToken.Token)
            .ToTryAsync()
            .Bind(chatMember => SendRestrictClearMessage(chatId, chatMember).ToTryAsync())
            .Match(
                _ => Log.Information("Sent restrict message to chat {0}", chatId),
                ex => Log.Error(ex, "Failed to send restrict message to chat {0}", chatId)
            );
        await Array(
            sendRestrictMessageTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)
        ).WhenAll();
        return
            await restrictRepository.DeleteRestrictMember(id);
    }

    private async Task<bool> AddRestrict(RestrictMember member, int messageId)
    {
        var (telegramId, chatId) = member.Id;
        var sendRestrictMessageTask = botClient.GetChatMember(chatId, telegramId, cancelToken.Token)
            .ToTryAsync()
            .Bind(chatMember => SendRestrictMessage(chatId, member, chatMember).ToTryAsync())
            .Match(
                _ => Log.Information("Sent restrict message to chat {0}", chatId),
                ex => Log.Error(ex, "Failed to send restrict message to chat {0}", chatId)
            );
        await Array(
            sendRestrictMessageTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)
        ).WhenAll();
        return await restrictRepository.AddRestrict(member);
    }

    private async Task<Unit> SendRestrictClearMessage(long chatId, ChatMember chatMember)
    {
        var message = string.Format(appConfig.RestrictConfig.RestrictClearMessage, chatMember.GetUsername());
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent restrict clear message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send restrict clear message to chat {0}", chatId), cancelToken.Token);
    }

    private async Task<Unit> SendRestrictMessage(long chatId, RestrictMember member, ChatMember chatMember)
    {
        var message = string.Format(appConfig.RestrictConfig.RestrictMessage, chatMember.GetUsername(),
            member.RestrictType);
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("{command}-message sent from {0} in chat {1} with type {2}", Command,
                member.Id.TelegramId,
                chatId, member.RestrictType
            ),
            ex => Log.Error(ex,
                "Failed to send {command} message from {0} in chat {1} with type {2}",
                Command, member.Id.TelegramId, chatId, member.RestrictType),
            cancelToken.Token);
    }
}