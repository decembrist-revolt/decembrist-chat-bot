using System.Text.RegularExpressions;
using DecembristChatBotSharp.Mongo;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

public class BanCommandHandler(
    AppConfig appConfig,
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    BotClient botClient,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public string Command => "/ban";
    public string Description => "Ban user in reply. Set reason in format /ban This is ban reason";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var chatId = parameters.ChatId;
        var telegramId = parameters.TelegramId;
        var messageId = parameters.MessageId;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        if (text != Command && !text.StartsWith("/ban ")) return unit;

        if (!await lockRepository.TryAcquire(chatId, Command, telegramId: telegramId))
        {
            return await messageAssistance.CommandNotReady(chatId, messageId, Command);
        }

        var maybeBanUsername = await parameters.ReplyToTelegramId.ToTryOption()
            .MapAsync(replyTelegramId => botClient.GetChatMember(chatId, replyTelegramId, cancelToken.Token))
            .Map(member => member.GetUsername());

        return await maybeBanUsername.MatchAsync(
            username => SendBanMessage(chatId, telegramId, messageId, text.Trim(), username),
            () => OnNoReceiver(chatId, messageId),
            ex =>
            {
                Log.Error(ex, "Failed to get chat member in chat {0} with telegramId {1}", chatId, telegramId);
                return Task.FromResult(unit);
            });
    }

    private async Task<Unit> SendBanMessage(
        long chatId,
        long telegramId,
        int messageId,
        string text,
        string banUsername)
    {
        var argsPosition = text.IndexOf(' ');
        var arg = argsPosition != -1 ? text[(argsPosition + 1)..] : string.Empty;

        var banConfig = appConfig.CommandConfig.BanConfig;
        if (string.IsNullOrEmpty(arg)) arg = banConfig.BanNoReasonMessage;
        arg = Regex.Replace(arg, @"\s+", " ");
        if (arg.Length > banConfig.ReasonLengthLimit)
        {
            return await Array(
                SendToLongReasonMessage(chatId, telegramId),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
        }
        
        var message = string.Format(banConfig.BanMessage, banUsername, arg);
        var rand = new Random();
        
        // 1/10 chance to send addition ban message
        if (rand.Next(10) == 0) message = message + "\n" + banConfig.BanAdditionMessage;

        return await Array(SendBanMessage(chatId, telegramId, message, arg),
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private async Task<Unit> SendBanMessage(long chatId, long telegramId, string message, string arg)
    {
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Banned message sent from {0} in chat {1} with reason {2}", telegramId, chatId, arg),
            ex => Log.Error(ex,
                "Failed to send ban message from {0} in chat {1} with reason {2}", telegramId, chatId, arg),
            cancelToken.Token);
    }

    private async Task<Unit> OnNoReceiver(long chatId, int messageId)
    {
        Log.Information("No receiver to ban in chat {0}", chatId);
        return await Array(
            SendBanReceiverNotSet(chatId),
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private async Task<Unit> SendBanReceiverNotSet(long chatId)
    {
        var message = appConfig.CommandConfig.BanConfig.BanReceiverNotSetMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent like receiver not set message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send like receiver not set message to chat {0}", chatId),
            cancelToken.Token);
    }
    
    private async Task<Unit> SendToLongReasonMessage(long chatId, long telegramId)
    {
        var banConfig = appConfig.CommandConfig.BanConfig;
        var message = string.Format(banConfig.ReasonLengthErrorMessage, banConfig.ReasonLengthLimit);
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent ban reason too long message to chat {0}", chatId),
            ex => Log.Error(ex,
                "Failed to send ban reason too long message to chat {0}", chatId),
            cancelToken.Token);
    }
}