using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class SlotMachineCommandHandler(
    AppConfig appConfig,
    MemberItemService memberItemService,
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    MemberItemRepository memberItemRepository,
    HistoryLogRepository historyLogRepository,
    MongoDatabase db,
    BotClient botClient,
    ExpiredMessageRepository expiredMessageRepository,
    Random random,
    CancellationTokenSource cancelToken,
    PremiumMemberService premiumMemberService) : ICommandHandler
{
    public const string CommandKey = "/slotmachine";

    public string Command => CommandKey;
    public string Description => appConfig.CommandConfig.CommandDescriptions.GetValueOrDefault(CommandKey, "Play slot machine for a chance to win boxes");
    public CommandLevel CommandLevel => CommandLevel.Item;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        var isAdmin = await adminUserRepository.IsAdmin(new(telegramId, chatId));

        var result = await memberItemService.UseSlotMachine(chatId, telegramId, isAdmin);
        if (result == SlotMachineResult.Failed)
        {
            result = await memberItemService.UseSlotMachine(chatId, telegramId, isAdmin);
        }

        return result switch
        {
            SlotMachineResult.Failed => await SendSlotMachineErrorMessage(chatId),
            SlotMachineResult.NoItems => await messageAssistance.SendNoItems(chatId),
            SlotMachineResult.Success => await Array(
                ProcessSlotResult(chatId, telegramId),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private async Task<Unit> ProcessSlotResult(long chatId, long telegramId)
    {
        var isPremium = await premiumMemberService.IsPremium(telegramId, chatId);
        var attempts = isPremium ? appConfig.SlotMachineConfig.PremiumAttempts : 1;

        var num1 = 0;
        var num2 = 0;
        var num3 = 0;
        var isWin = false;

        // Try multiple times for premium users
        for (var i = 0; i < attempts; i++)
        {
            (num1, num2, num3) = GenerateSlotNumbers(telegramId, chatId);
            isWin = num1 == num2 && num2 == num3;

            if (isWin)
            {
                Log.Information("User {0} won on attempt {1}/{2}", telegramId, i + 1, attempts);
                break;
            }
        }

        var username = await botClient.GetUsername(chatId, telegramId, cancelToken.Token)
            .ToAsync()
            .IfNone(telegramId.ToString);

        var emoji1 = GetNumberEmoji(num1);
        var emoji2 = GetNumberEmoji(num2);
        var emoji3 = GetNumberEmoji(num3);

        var resultText = isWin
            ? string.Format(appConfig.SlotMachineConfig.WinMessage, num1)
            : string.Format(appConfig.SlotMachineConfig.LoseMessage, appConfig.SlotMachineConfig.PremiumAttempts);

        var message = string.Format(appConfig.SlotMachineConfig.LaunchMessage, username, emoji1, emoji2, emoji3,
            resultText);

        var is777 = num1 == 7 && num2 == 7 && num3 == 7;
        if (isWin)
        {
            using var session = await db.OpenSession();
            session.StartTransaction();

            await memberItemRepository.AddMemberItem(chatId, telegramId, MemberItemType.Box, session, num1);
            // Give premium days for 777
            if (is777)
            {
                await premiumMemberService.AddPremiumMember(
                    chatId, telegramId, PremiumMemberOperationType.AddBySlotMachine,
                    DateTime.UtcNow.AddDays(appConfig.SlotMachineConfig.PremiumDaysFor777), session: session);
            }

            await historyLogRepository.LogItem(
                chatId, telegramId, MemberItemType.Box, num1,
                MemberItemSourceType.SlotMachine, session);

            await session.TryCommit(cancelToken.Token);
        }

        const string logTemplate = "Slot machine result {0} for {1} in chat {2}: [{3}, {4}, {5}]";
        if (is777)
        {
            message += string.Format(
                "\n\n" + appConfig.SlotMachineConfig.Premium777Message, appConfig.SlotMachineConfig.PremiumDaysFor777);
        }

        await botClient.SendMessageAndLog(chatId, message,
            m =>
            {
                Log.Information(logTemplate, isWin ? "win" : "lose", telegramId, chatId, num1, num2, num3);
                if (!isWin) expiredMessageRepository.QueueMessage(chatId, m.MessageId);
            },
            ex => Log.Error(ex, logTemplate, "failed", telegramId, chatId, num1, num2, num3),
            cancelToken.Token);

        return unit;
    }

    private async Task<Unit> SendSlotMachineErrorMessage(long chatId)
    {
        var message = appConfig.SlotMachineConfig.ErrorMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            sentMessage =>
            {
                Log.Information("Sent slot machine error message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, sentMessage.MessageId);
            },
            ex => Log.Error(ex, "Failed to send slot machine error message to chat {0}", chatId),
            cancelToken.Token);
    }

    private static string GetNumberEmoji(int number) => number switch
    {
        1 => "1️⃣",
        2 => "2️⃣",
        3 => "3️⃣",
        4 => "4️⃣",
        5 => "5️⃣",
        6 => "6️⃣",
        7 => "7️⃣",
        _ => number.ToString()
    };

    private (int, int, int) GenerateSlotNumbers(long telegramId, long chatId)
    {
        var num1 = random.Next(1, 8); // 1 to 7 inclusive
        var num2 = random.Next(1, 8);
        var num3 = random.Next(1, 8);

        Log.Information("Generated slot numbers for user {0} in chat {1}: [{2}, {3}, {4}]",
            telegramId, chatId, num1, num2, num3);

        return (num1, num2, num3);
    }
}