using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class ChatEditedHandler(
    CharmRepository charmRepository,
    RestrictHandler restrictHandler,
    BotClient botClient,
    CancellationTokenSource cancelToken)
{
    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;
        if (string.IsNullOrEmpty(text)) return unit;

        var (messageId, telegramId, chatId) = parameters;

        if (await restrictHandler.Do(parameters)) return unit;
        await HandleCharm(messageId, chatId, telegramId, text);

        return unit;
    }

    private async Task<bool> HandleCharm(int messageId, long chatId, long telegramId, string text)
    {
        var maybeMember = await charmRepository.GetCharmMember((telegramId, chatId));
        return await maybeMember
            .Filter(member => member.SecretWord != text && member.SecretMessageId == messageId)
            .MatchAsync(
                None: () => Task.FromResult(false),
                Some: async _ =>
                {
                    await botClient.DeleteMessageAndLog(chatId, messageId,
                        () => Log.Information("Deleted secret message charm user:{0} in chat:{1} ", telegramId, chatId),
                        ex => Log.Error(ex, "Failed to delete secret message: charm user:{0} in chat:{1}",
                            telegramId, chatId),
                        cancelToken.Token);
                    return true;
                });
    }
}