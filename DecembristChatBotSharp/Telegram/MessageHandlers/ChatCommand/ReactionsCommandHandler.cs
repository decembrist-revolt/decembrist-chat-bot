using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
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
    ReactionRepository reactionRepository,
    MessageAssistance messageAssistance,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public string Command { get; } = "/spamreact";

    public string Description { get; } = "react spam";


    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;
        var emojiText = ParseEmoji(text);
        var emoji = new ReactionTypeEmoji
        {
            Emoji = "🐳"
        };
        var replyUserId = parameters.ReplyToTelegramId;
        if (replyUserId.IsNone)
        {
            Log.Warning("Reply user for {0} not set in chat {1}", Command, chatId);
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
        }

        var receiverId = replyUserId.ValueUnsafe();
        await reactionRepository.AddReactionMember(new ReactionMember(new(receiverId, chatId), emoji));
        await botClient.SetMessageReaction(chatId, messageId, (List<ReactionTypeEmoji>) [emoji],
            cancellationToken: cancelToken.Token);
        return unit;
    }

    private ReactionTypeEmoji ParseEmoji(string text)
    {
        throw new NotImplementedException();
    }
}