using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using JasperFx.Core;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

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
            .MatchAsync(async member =>
                {
                    var logTemplate = "Reaction {0} ChatId: {1} MessageId: {2} TelegramId: {3}";
                    await botClient.SetReactionAndLog(chatId, messageId, (List<ReactionTypeEmoji>) [member.Emoji],
                        onSent: _ =>
                        {
                            // Log.Information(logTemplate, "added", chatId, messageId, member.Id.TelegramId);
                            return;
                        },
                        onError: ex =>
                            Log.Error(ex, logTemplate, "failed", chatId, messageId, member.Id.TelegramId),
                        cancelToken: cancelToken.Token);
                    return true;
                },
                () => false);
    }
}