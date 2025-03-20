using System.Text.RegularExpressions;
using DecembristChatBotSharp.Mongo;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

public class WrongCommandHandler(
    AppConfig appConfig,
    BotClient botClient,
    MessageAssistance messageAssistance,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken
)
{
    private const string Pattern = "^/([A-Za-z0-9].*)?$";

    public async Task<bool> Do(long chatId, string text, int messageId)
    {
        if (!Regex.IsMatch(text, Pattern)) return false;
        
        var message = appConfig.CommandConfig.WrongCommandMessage;
        await Array(SendWrongCommandMessage(chatId, message),
            messageAssistance.DeleteCommandMessage(chatId, messageId, text)).WhenAll();

        return true;
    }

    private async Task<Unit> SendWrongCommandMessage(long chatId, string message) =>
        await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent wrong command message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send wrong command message to chat {0}", chatId),
            cancelToken.Token);
}