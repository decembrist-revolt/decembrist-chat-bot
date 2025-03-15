using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.MessageHandlers;

public class ChatBotAddHandler(AppConfig appConfig, BotClient botClient)
{
    public async Task<Unit> Do(
        long chatId,
        CancellationToken cancelToken)
    {
        var (allowedChatIds, wrongChatText, rightChatText) = appConfig.AllowedChatConfig;
        return allowedChatIds?.Contains(chatId) == true
            ? await RightChatAdd(chatId, rightChatText, cancelToken)
            : await WrongChatAdd(chatId, wrongChatText, cancelToken);
    }

    private async Task<Unit> WrongChatAdd(long chatId, string wrongChatText, CancellationToken cancelToken)
    {
        return await TryAsync(Task.WhenAll(
            botClient.SendMessage(chatId, wrongChatText, cancellationToken: cancelToken),
            botClient.LeaveChat(chatId, cancellationToken: cancelToken)
        )).Match(
            _ => Log.Information("Sent wrong chat message and left chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send wrong chat message and left chat {0}", chatId));
    }

    private async Task<Unit> RightChatAdd(long chatId, string rightChatText, CancellationToken cancelToken) =>
        await TryAsync(botClient.SendMessage(chatId, rightChatText, cancellationToken: cancelToken))
            .Match(
                _ => Log.Information("Sent right chat message to in chat {0}", chatId),
                ex => Log.Error(ex, "Failed to send right chat message in chat {0}", chatId));
}