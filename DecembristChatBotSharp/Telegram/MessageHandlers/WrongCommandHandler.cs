using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class WrongCommandHandler(
    BotClient botClient,
    MessageAssistance messageAssistance,
    ExpiredMessageRepository expiredMessageRepository,
    ChatConfigService chatConfigService,
    CancellationTokenSource cancelToken
)
{
    private const string Pattern = "^/([A-Za-z0-9].*)?$";

    public async Task<bool> Do(long chatId, string text, int messageId)
    {
        if (!Regex.IsMatch(text, Pattern)) return false;
        
        var maybeCommandConfig = await chatConfigService.GetConfig(chatId, config => config.CommandConfig);
        if (!maybeCommandConfig.TryGetSome(out var commandConfig)) return false;

        var message = commandConfig.WrongCommandMessage;
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