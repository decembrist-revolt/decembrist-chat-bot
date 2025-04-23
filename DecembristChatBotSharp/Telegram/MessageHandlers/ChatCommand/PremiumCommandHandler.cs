using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class PremiumCommandHandler(
    AppConfig appConfig,
    MessageAssistance messageAssistance,
    AdminUserRepository adminUserRepository,
    PremiumMemberService premiumMemberService,
    PremiumMemberRepository premiumMemberRepository,
    BotClient botClient,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    private static readonly Regex DaysRegex = new(@"^days@(\d+)$", RegexOptions.Compiled);

    public const string RemoveSubcommand = "remove";

    public string Command => "/premium";
    public string Description => "Premium membership command";
    public CommandLevel CommandLevel => CommandLevel.User;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var isAdmin = await adminUserRepository.IsAdmin((telegramId, chatId));
        if (!isAdmin) return await DoMemberPremiumCommand(telegramId, chatId, messageId);

        if (parameters.ReplyToTelegramId.IsNone)
        {
            Log.Warning("Reply user for {0} not set in chat {1}", Command, chatId);
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
        }

        return await parameters.ReplyToTelegramId.IfSomeAsync(premiumMemberId =>
            DoAdminPremiumCommand(premiumMemberId, chatId, messageId, text, telegramId));
    }

    private async Task<Unit> DoAdminPremiumCommand(
        long premiumMemberId, long chatId, int messageId, string text, long adminTelegramId)
    {
        if (!text.Contains(' '))
        {
            var days = 365;
            var result = await premiumMemberService.AddPremiumMember(
                chatId,
                premiumMemberId,
                PremiumMemberOperationType.AddByAdmin,
                DateTime.UtcNow.AddDays(days),
                sourceTelegramId: adminTelegramId);

            return await HandleAddResult(premiumMemberId, chatId, messageId, result, days);
        }
        else if (text.Split(' ') is [_, var subCommand])
        {
            return subCommand switch
            {
                RemoveSubcommand => await RemovePremiumMember(chatId, premiumMemberId, messageId, adminTelegramId),
                _ when GetDays(subCommand) is var days && days > 0 =>
                    await AddPremiumMember(chatId, premiumMemberId, days, messageId, text, adminTelegramId),
                _ => WrongCommandFormat(chatId, text)
            };
        }

        return WrongCommandFormat(chatId, text);
    }

    private async Task<Unit> HandleAddResult(
        long premiumMemberId, long chatId, int messageId, AddPremiumMemberResult result, int days)
    {
        if (result == AddPremiumMemberResult.Success)
        {
            return await Array(
                SendAddPremiumMessage(chatId, premiumMemberId, days),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
        }

        if (result == AddPremiumMemberResult.Duplicate)
        {
            Log.Warning("Member {0} already has premium in chat {1}", premiumMemberId, chatId);
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
        }

        // ignore error
        return unit;
    }

    private int GetDays(string subCommand) => DaysRegex.Match(subCommand) is { Success: true } match
        ? int.Parse(match.Groups[1].Value)
        : 0;

    private async Task<Unit> DoMemberPremiumCommand(long telegramId, long chatId, int messageId)
    {
        var isPremium = await premiumMemberRepository.IsPremium((telegramId, chatId));
        var sendTask = isPremium
            ? SendImPremiumMessage(chatId, messageId)
            : SendNotPremiumMessage(chatId, messageId);

        return await Array(
            expiredMessageRepository.QueueMessage(chatId, messageId),
            sendTask).WhenAll();
    }

    private async Task<Unit> RemovePremiumMember(long chatId, long telegramId, int messageId, long adminTelegramId)
    {
        var result = await premiumMemberService.RemovePremiumMember(
            chatId, telegramId, PremiumMemberOperationType.RemoveByAdmin, sourceTelegramId: adminTelegramId);
        if (result)
        {
            return await Array(
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
                SendRemovePremiumMessage(chatId, telegramId)).WhenAll();
        }

        return unit;
    }

    private async Task<Unit> AddPremiumMember(
        long chatId, long premiumMemberId, int days, int messageId, string text, long adminTelegramId)
    {
        var addResult = await premiumMemberService.AddPremiumMember(
            chatId,
            premiumMemberId,
            PremiumMemberOperationType.AddByAdmin,
            DateTime.UtcNow.AddDays(days),
            sourceTelegramId: adminTelegramId);

        return await HandleAddResult(premiumMemberId, chatId, messageId, addResult, days);
    }

    private Unit WrongCommandFormat(long chatId, string text)
    {
        Log.Warning("Wrong premium command format {0} in chat {1}", text, chatId);
        return unit;
    }

    private async Task<Unit> SendAddPremiumMessage(long chatId, long telegramId, int days)
    {
        var maybeUsername = await botClient.GetUsername(chatId, telegramId, cancelToken.Token);
        var username = maybeUsername.IfNone("Anonymous");
        var message = string.Format(appConfig.CommandConfig.PremiumConfig.AddPremiumMessage, username, days);
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent add premium message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send add premium message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendRemovePremiumMessage(long chatId, long telegramId)
    {
        var maybeUsername = await botClient.GetUsername(chatId, telegramId, cancelToken.Token);
        var username = maybeUsername.IfNone("Anonymous");
        var message = string.Format(appConfig.CommandConfig.PremiumConfig.RemovePremiumMessage, username);
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent remove premium message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send remove premium message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendImPremiumMessage(long chatId, int messageId)
    {
        var message = appConfig.CommandConfig.PremiumConfig.ImPremiumMessage;
        return await botClient.SendMessageAndLog(chatId, message, messageId,
            message =>
            {
                Log.Information("Sent im premium message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.Id);
            },
            ex => Log.Error(ex, "Failed to send im premium message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendNotPremiumMessage(long chatId, int messageId)
    {
        var message = appConfig.CommandConfig.PremiumConfig.NotPremiumMessage;
        return await botClient.SendMessageAndLog(chatId, message, messageId,
            message =>
            {
                Log.Information("Sent im premium message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.Id);
            },
            ex => Log.Error(ex, "Failed to send im premium message to chat {0}", chatId),
            cancelToken.Token);
    }
}