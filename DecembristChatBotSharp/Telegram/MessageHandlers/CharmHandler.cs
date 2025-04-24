using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class CharmHandler(
    CharmRepository db,
    AdminUserRepository adminUserRepository,
    BotClient botClient,
    CancellationTokenSource cancelToken)
{
    private const string Reaction = "\ud83d\udc40";
    
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        return await db.GetCharmMember((telegramId, chatId))
            .MatchAsync(
                member => HandleMessage(chatId, telegramId, messageId, member, parameters.Payload),
                () => false);
    }

    async Task<bool> HandleMessage(
        long chatId, long telegramId, int messageId, CharmMember member, IMessagePayload payload)
    {
        if (member.SecretMessageId is { } secretMessageId)
        {
            var isMessageExists = await SetMessageReaction(chatId, secretMessageId);
            if (isMessageExists) return false;
            
            Log.Information("Charm user: {0} in chat: {1} message deleted", telegramId, chatId);
            await db.SetSecretMessageId((telegramId, chatId), null);
        }

        if (payload is TextPayload { Text: var text })
        {
            if (text.StartsWith('/') && await adminUserRepository.IsAdmin((telegramId, chatId))) return false;

            if (text == member.SecretWord)
            {
                await db.SetSecretMessageId((telegramId, chatId), messageId);
                await SetMessageReaction(chatId, messageId);
                Log.Information("Clear charm user: {0} in chat: {1}", telegramId, chatId);

                return false;
            }
        }

        await botClient.DeleteMessageAndLog(chatId, messageId,
            () => Log.Information("Deleted charm user: {0} message in chat {1}", telegramId, chatId),
            ex => Log.Error(ex, "Failed to delete charm user: {0} message in chat {1}", telegramId, chatId),
            cancelToken.Token);
        return true;
    }

    private async Task<bool> SetMessageReaction(long chatId, int secretMessageId) => await botClient.SetMessageReaction(
            chatId, secretMessageId, [Reaction], cancellationToken: cancelToken.Token)
        .ToTryAsync()
        .Map(_ => true)
        .IfFail(false);
}