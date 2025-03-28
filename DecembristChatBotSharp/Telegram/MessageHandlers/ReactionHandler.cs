using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class ReactionHandler(
    BotClient botClient,
    ReactionRepository db)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        return await db.GetReactionMember(new ReactionMember.CompositeId(telegramId, chatId))
            .MatchAsync(async member =>
                {
                    await botClient.SetMessageReaction(chatId, messageId, (List<ReactionTypeEmoji>) [member.Emoji]);
                    return true;
                },
                () => false);
    }
}