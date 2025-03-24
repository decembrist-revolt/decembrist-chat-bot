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
    CancellationTokenSource cancelToken,
    AppConfig appConfig,
    ExpiredMessageRepository expiredMessageRepository,
    MessageAssistance messageAssistance
) : ICommandHandler
{
    private readonly Regex _regex = new(@"\b(all|text|sticker|link|emoji)\b", RegexOptions.IgnoreCase);

    public string Command => "/restrict";
    public string Description => "Restrict user in reply";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        if (text != Command && !text.StartsWith(Command + " ")) return unit;
        if (!await restrictRepository.IsAdmin(telegramId))
        {
            return await messageAssistance.SendAdminOnlyMessage(chatId, telegramId);
        }

        var replyUserId = parameters.ReplyToTelegramId;
        if (replyUserId.IsNone)
        {
            return await Array(
                SendReceiverNotSet(chatId),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
        }

        var receiverId = replyUserId.ValueUnsafe();
        if (telegramId == receiverId)
        {
            return await Array(
                SendSelfRestrictMessage(chatId),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)
            ).WhenAll();
        }

        var id = new RestrictMember.CompositeId(receiverId, chatId);
        if (text.Contains("clear", StringComparison.OrdinalIgnoreCase))
        {
            return await DeleteRestrict(id, messageId);
        }

        var member = new RestrictMember
        {
            Id = id,
            RestrictType = await ParseRestrictType(text)
        };

        return await AddRestrict(member, messageId);
    }

    private async Task<RestrictType> ParseRestrictType(string input)
    {
        var result = RestrictType.All;
        var matches = _regex.Matches(input);
        foreach (Match match in matches)
        {
            if (Enum.TryParse(match.Value, true, out RestrictType restrictType))
            {
                if (restrictType == RestrictType.All) return RestrictType.All;
                result |= restrictType;
            }
        }

        return result;
    }

    private async Task<Unit> DeleteRestrict(RestrictMember.CompositeId id, int messageId)
    {
        var (telegramId, chatId) = id;
        var sendRestrictMessageTask = botClient.GetChatMember(chatId, telegramId, cancelToken.Token)
            .ToTryAsync()
            .Bind(chatMember => SendRestrictClearMessage(chatId, chatMember).ToTryAsync())
            .Match(
                _ => Log.Information("Sent recent message to chat {0}", chatId),
                ex => Log.Error(ex, "Failed to send recent message to chat {0}", chatId)
            );
        return await Array(
            restrictRepository.DeleteMember(id),
            sendRestrictMessageTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)
        ).WhenAll();
    }

    private async Task<Unit> AddRestrict(RestrictMember member, int messageId)
    {
        var (telegramId, chatId) = member.Id;
        var sendRestrictMessageTask = botClient.GetChatMember(chatId, telegramId, cancelToken.Token)
            .ToTryAsync()
            .Bind(chatMember => SendRestrictMessage(chatId, member, chatMember).ToTryAsync())
            .Match(
                _ => Log.Information("Sent recent message to chat {0}", chatId),
                ex => Log.Error(ex, "Failed to send recent message to chat {0}", chatId)
            );
        return await Array(
            sendRestrictMessageTask,
            restrictRepository.AddRestrict(member),
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)
        ).WhenAll();
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

    private async Task<Unit> SendSelfRestrictMessage(long chatId)
    {
        var message = appConfig.RestrictConfig.RestrictSelfMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent self restrict message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send self restrict message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendReceiverNotSet(long chatId)
    {
        var message = appConfig.RestrictConfig.RestrictReceiverNotSet;
        return await botClient.SendReceiverNotSetAndLog(chatId, message, nameof(RestrictCommandHandler),
            expiredMessageRepository, cancelToken.Token);
    }
}