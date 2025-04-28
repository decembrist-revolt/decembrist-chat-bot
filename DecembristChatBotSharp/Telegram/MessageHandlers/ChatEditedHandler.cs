using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class ChatEditedHandler(
    CharmRepository charmRepository,
    BotClient botClient,
    CancellationTokenSource cancelToken)
{
    public async Task<Unit> Do(Message message)
    {
        var chatId = message.Chat.Id;
        var telegramId = message.From!.Id;
        var messageText = message.Text ?? message.Caption;
        if (!string.IsNullOrEmpty(messageText))
        {
            await HandleCharm(message.MessageId, chatId, telegramId, messageText);
        }

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