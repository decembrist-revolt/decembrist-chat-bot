using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class CharmHandler(
    CharmRepository db,
    AdminUserRepository adminUserRepository,
    BotClient botClient,
    CancellationTokenSource cancelToken)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        return await db.GetCharmMember((telegramId, chatId))
            .MatchAsync(
                member => HandleMessage(chatId, telegramId, messageId, member.SecretWord, parameters.Payload),
                () => false);
    }

    async Task<bool> HandleMessage(long chatId, long telegramId, int messageId, string secretWord,
        IMessagePayload payload)
    {
        if (payload is TextPayload { Text: var text })
        {
            if (text == secretWord)
            {
                await db.DeleteCharmMember((telegramId, chatId));
                Log.Information("Clear charm user: {0} in chat: {1}", telegramId, chatId);
                return false;
            }

            if (await adminUserRepository.IsAdmin((telegramId, chatId)) && text.StartsWith('/'))
            {
                return false;
            }
        }

        await botClient.DeleteMessageAndLog(chatId, messageId,
            () => Log.Information("Deleted charm user: {0} message in chat {1}", telegramId, chatId),
            ex => Log.Error(ex, "Failed to delete charm user: {0} message in chat {1}", telegramId, chatId),
            cancelToken.Token);
        return true;
    }
}