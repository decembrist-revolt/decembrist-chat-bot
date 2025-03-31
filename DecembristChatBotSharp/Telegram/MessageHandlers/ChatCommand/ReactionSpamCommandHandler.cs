using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class ReactionSpamCommandHandler(
    BotClient botClient,
    AppConfig appConfig,
    AdminUserRepository adminUserRepository,
    ReactionSpamRepository reactionSpamRepository,
    MessageAssistance messageAssistance,
    MemberItemService itemService,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public const string CommandKey = "/reactionspam";
    public string Command => CommandKey;

    public string Description => "Adds reactions to the user's messages";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var replyUserId = parameters.ReplyToTelegramId;
        if (replyUserId.IsNone)
        {
            await SendHelpMessage(chatId);
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
        }

        var receiverId = replyUserId.ValueUnsafe();

        var isAdmin = await adminUserRepository.IsAdmin(new(telegramId, chatId));
        var maybeEmoji = ParseEmoji(text.Substring(Command.Length).Trim());
        if (isAdmin && text.Contains("clear", StringComparison.OrdinalIgnoreCase))
        {
            if (await reactionSpamRepository.DeleteReactionSpamMember(new(receiverId, chatId)))
                Log.Information("Clear react spam member for {0} in chat {1} by {2}", receiverId, chatId,
                    telegramId);
            else
                Log.Error("React spam member not cleared for {0} in chat {1} by {2}", receiverId, chatId,
                    telegramId);
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
        }

        if (maybeEmoji.IsNone)
        {
            await SendHelpMessage(chatId);
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
        }

        var emoji = maybeEmoji.ValueUnsafe();

        var expireAt = DateTime.UtcNow.AddMinutes(appConfig.ReactionSpamConfig.DurationMinutes);
        var reactMember = new ReactionSpamMember(new(receiverId, chatId), emoji, expireAt);

        var result = await itemService.UseReactionSpam(chatId, telegramId, reactMember, isAdmin);

        await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
        return result switch
        {
            ReactionSpamResult.NoItems => await messageAssistance.SendNoItems(chatId),
            ReactionSpamResult.Failed => await SendHelpMessage(chatId),
            ReactionSpamResult.Duplicate => await SendDuplicateMessage(chatId),
            ReactionSpamResult.Success => await SendSuccessMessage(reactMember.Id, emoji.Emoji),
            _ => unit
        };
    }

    private async Task<Unit> SendDuplicateMessage(long chatId)
    {
        var message = appConfig.ReactionSpamConfig.DuplicateMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            m =>
            {
                Log.Information("Sent reaction spam duplicate message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, m.MessageId);
            },
            ex => Log.Error(ex, "Failed to send fast reply duplicate message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendHelpMessage(long chatId)
    {
        var message = string.Format(appConfig.ReactionSpamConfig.HelpMessage, Command, EmojisString);
        return await botClient.SendMessageAndLog(chatId, message,
            (m) =>
            {
                Log.Information("Sent reaction spam help message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, m.MessageId);
            },
            ex => Log.Error(ex, "Failed to send react spam help message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendSuccessMessage(CompositeId id, string emoji)
    {
        var (receiverId, chatId) = id;
        return await botClient
            .GetUsername(chatId, receiverId, cancelToken.Token)
            .IfSomeAsync(async username =>
            {
                var message = string.Format(appConfig.ReactionSpamConfig.SuccessMessage, emoji, username);
                var logTemplate = "Reaction spam message sent {0} ChatId: {1}, Emoji:{2} Receiver: {3}";
                return await botClient.SendMessageAndLog(chatId, message,
                    m =>
                    {
                        Log.Information(logTemplate, "success", chatId, emoji, receiverId);
                        expiredMessageRepository.QueueMessage(chatId, m.MessageId);
                    },
                    ex => Log.Error(ex, logTemplate, "failed", chatId, emoji, receiverId),
                    cancelToken.Token);
            });
    }

    private Option<ReactionTypeEmoji> ParseEmoji(string text)
    {
        var isEmoji = Emojis.Contains(text);
        return isEmoji
            ? new ReactionTypeEmoji
            {
                Emoji = text
            }
            : None;
    }

    private static readonly System.Collections.Generic.HashSet<string> Emojis =
    [
        "👍", "👎", "❤", "🔥", "🥰", "👏", "😁", "🤔", "🤯", "😱", "🤬", "😢", "🎉", "🤩", "🤮", "💩", "🙏", "👌", "🕊",
        "🤡", "🥱", "🥴", "😍", "🐳", "❤‍", "🌚", "🌭", "💯", "🤣", "⚡", "🍌", "🏆", "💔", "🤨", "😐", "🍓", "🍾", "💋",
        "🖕", "😈", "😴", "😭", "🤓", "👻", "👨", "‍💻", "👀", "🎃", "🙈", "😇", "😨", "🤝", "✍", "🤗", "🫡", "🎅",
        "🎄", "☃", "💅", "🤪", "🗿", "🆒", "💘", "🙉", "🦄", "😘", "💊", "🙊", "😎", "👾", "🤷‍♂", "🤷", "🤷‍♀", "😡"
    ];

    private static readonly string EmojisString = string.Join(", ", Emojis);
}