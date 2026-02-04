using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class BanCommandHandler(
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    MemberItemRepository memberItemRepository,
    BotClient botClient,
    Random random,
    CancellationTokenSource cancelToken,
    ChatConfigService chatConfigService) : ICommandHandler
{
    public string Command => "/ban";
    public string Description => "Ban user in reply. Set reason in format /ban This is ban reason";
    public CommandLevel CommandLevel => CommandLevel.User;

    [GeneratedRegex(@"\s+")]
    private static partial Regex CommandRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;
        var maybeConfig = await chatConfigService.GetConfig(chatId, config => config.BanConfig);
        if (!maybeConfig.TryGetSome(out var banConfig))
        {
            await messageAssistance.SendNotConfigured(chatId, messageId, Command);
            return chatConfigService.LogNonExistConfig(unit, nameof(BanConfig), Command);
        }

        if (!await lockRepository.TryAcquire(chatId, Command, telegramId: telegramId))
        {
            return await messageAssistance.CommandNotReady(chatId, messageId, Command);
        }

        var taskResult = parameters.ReplyToTelegramId.ToTryOption()
            .MatchAsync(
                receiverId => HandleBan(chatId, telegramId, receiverId, messageId, text, banConfig),
                () => SendReceiverNotSet(chatId, banConfig),
                ex =>
                {
                    Log.Error(ex, "Failed to get chat member in chat {0} with telegramId {1}", chatId, telegramId);
                    return Task.FromResult(unit);
                }
            );
        return await Array(taskResult,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private async Task<Unit> HandleBan(long chatId, long telegramId, long receiverId, int messageId, string text,
        BanConfig banConfig)
    {
        var targetHasAmulet = await memberItemRepository.IsUserHasItem(chatId, receiverId, MemberItemType.Amulet);
        return targetHasAmulet
            ? await SendAmuletMessage(chatId, banConfig)
            : await SendBanMessage(chatId, telegramId, text.Trim(), receiverId, banConfig);
    }


    private async Task<Unit> SendBanMessage(
        long chatId,
        long telegramId,
        string text,
        long receiverId,
        BanConfig banConfig)
    {
        var banUsername = await botClient.GetUsername(chatId, receiverId, cancelToken.Token)
            .ToAsync()
            .IfNone(receiverId.ToString);

        var argsPosition = text.IndexOf(' ');
        var arg = argsPosition != -1 ? text[(argsPosition + 1)..] : string.Empty;

        if (string.IsNullOrEmpty(arg))
        {
            arg = banConfig.BanNoReasonMessage;
        }
        else
        {
            arg = CommandRegex().Replace(arg, " ");

            if (arg.Length > banConfig.ReasonLengthLimit)
            {
                return await SendToLongReasonMessage(chatId, banConfig);
            }
        }

        var message = string.Format(banConfig.BanMessage, banUsername, arg);

        // 1/10 chance to send addition ban message
        if (random.Next(10) == 0) message = message + "\n" + banConfig.BanAdditionMessage;

        return await SendBanMessage(chatId, telegramId, message, arg);
    }

    private async Task<Unit> SendBanMessage(long chatId, long telegramId, string message, string arg)
    {
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Banned message sent from {0} in chat {1} with reason {2}", telegramId, chatId, arg),
            ex => Log.Error(ex,
                "Failed to send ban message from {0} in chat {1} with reason {2}", telegramId, chatId, arg),
            cancelToken.Token);
    }

    private async Task<Unit> SendReceiverNotSet(long chatId, BanConfig banConfig)
    {
        var message = banConfig.BanReceiverNotSetMessage;
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendToLongReasonMessage(long chatId, BanConfig banConfig)
    {
        var message = string.Format(banConfig.ReasonLengthErrorMessage, banConfig.ReasonLengthLimit);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendAmuletMessage(long chatId, BanConfig banConfig)
    {
        var message = banConfig.BanAmuletMessage;
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }
}