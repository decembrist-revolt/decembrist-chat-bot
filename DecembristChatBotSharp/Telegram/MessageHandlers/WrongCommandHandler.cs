using System.Text.RegularExpressions;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public partial class WrongCommandHandler(
    BotClient botClient,
    MessageAssistance messageAssistance,
    ExpiredMessageRepository expiredMessageRepository,
    ChatConfigService chatConfigService,
    CancellationTokenSource cancelToken
)
{
    [GeneratedRegex("^/([A-Za-z0-9].*)?$")]
    private static partial Regex CommandRegex();

    public async Task<bool> Do(long chatId, string text, int messageId)
    {
        if (!CommandRegex().IsMatch(text)) return false;
        
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