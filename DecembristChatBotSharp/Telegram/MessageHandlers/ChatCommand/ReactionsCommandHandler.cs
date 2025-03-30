using System.Text;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class ReactionsCommandHandler(
    BotClient botClient,
    AppConfig appConfig,
    AdminUserRepository adminUserRepository,
    ReactionRepository reactionRepository,
    MessageAssistance messageAssistance,
    MemberItemService itemService,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public string Command => "/reactspam";

    public string Description => "react spam";


    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var maybeEmoji = ParseEmoji(text.Substring(Command.Length).Trim());
        var replyUserId = parameters.ReplyToTelegramId;
        if (replyUserId.IsNone || maybeEmoji.IsNone)
        {
            Log.Warning("Reply user for {0} not set in chat {1}", Command, chatId);
            await SendHelpMessage(chatId);
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
        }

        var emoji = maybeEmoji.ValueUnsafe();
        var isAdmin = await adminUserRepository.IsAdmin(new(telegramId, chatId));
        var receiverId = replyUserId.ValueUnsafe();
        var result = await itemService.UseSpamReactItem(chatId, telegramId,
            new ReactionMember(new(receiverId, chatId), emoji, DateTime.UtcNow), isAdmin);

        return result switch
        {
            UseFastReplyResult.NoItems => await messageAssistance.SendNoItems(chatId),
            UseFastReplyResult.Failed => await SendHelpMessage(chatId),
            UseFastReplyResult.Success =>
                await botClient.SetMessageReaction(chatId, messageId, (List<ReactionTypeEmoji>) [emoji],
                    cancellationToken: cancelToken.Token).UnitTask(),
            _ => unit
        };
        //todo SetMessageReaction ex and log
        return unit;
    }

    private async Task<Unit> SendHelpMessage(long chatId)
    {
        var message = appConfig.EmojiConfig.DefaultEmoji;
        return await botClient.SendMessageAndLog(chatId, message,
            (m) => { Log.Information("Sent fast reply help message to chat {0}", chatId); },
            ex => Log.Error(ex, "Failed to send fast reply help message to chat {0}", chatId),
            cancelToken.Token);
    }

    private Option<ReactionTypeEmoji> ParseEmoji(string text)
    {
        var isEmoji = _emojis.Contains(text);
        Log.Information("emoji is {0}", isEmoji);
        return isEmoji
            ? new ReactionTypeEmoji
            {
                Emoji = text
            }
            : None;
    }

    private readonly List<string> _emojis =
    [
        "👍", "👎", "❤", "🔥", "🥰", "👏", "😁", "🤔", "🤯", "😱", "🤬", "😢", "🎉", "🤩", "🤮", "💩", "🙏", "👌",
        "🕊", "🤡", "🥱", "🥴", "😍", "🐳", "❤‍🔥", "🌚", "🌭", "💯", "🤣", "⚡", "🍌", "🏆", "💔", "🤨", "😐", "🍓",
        "🍾", "💋", "🖕", "😈", "😴", "😭", "🤓", "👻", "👨‍💻", "👀", "🎃", "🙈", "😇", "😨", "🤝", "✍", "🤗", "🫡",
        "🎅", "🎄", "☃", "💅", "🤪", "🗿", "🆒", "💘", "🙉", "🦄", "😘", "💊", "🙊", "😎", "👾", "🤷‍♂", "🤷", "🤷‍♀",
        "😡"
    ];
}