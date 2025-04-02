using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class ReactionSpamHandler(
    BotClient botClient,
    ReactionSpamRepository db,
    CancellationTokenSource cancelToken)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        return await db.GetReactionSpamMember(new(telegramId, chatId))
            .MatchAsync(member => SetReaction(member, messageId),
            () => false);
    }
    
    async Task<bool> SetReaction(ReactionSpamMember member, int messageId)
    {
        const string logTemplate = "Reaction {0} ChatId: {1} MessageId: {2} TelegramId: {3}";
        var chatId = member.Id.ChatId;
        await botClient.SetReactionAndLog(chatId, messageId, [member.Emoji], 
            _ => Log.Information(logTemplate, "added", chatId, messageId, member.Id.TelegramId), 
            ex => Log.Error(ex, logTemplate, "failed", chatId, messageId, member.Id.TelegramId), 
            cancelToken.Token);

        return true;
    }
}