using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class OpenBoxCommandHandler(
    AppConfig appConfig,
    MongoDatabase db,
    ExpiredMessageRepository expiredMessageRepository,
    MemberItemRepository memberItemRepository,
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    BotClient botClient,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public const string CommandKey = "/openbox";
    
    public string Command => CommandKey;
    public string Description => "Open surprise box if you have one";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var chatId = parameters.ChatId;
        var telegramId = parameters.TelegramId;
        var messageId = parameters.MessageId;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        if (await adminUserRepository.IsAdmin(telegramId))
        {
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
        }

        if (!await lockRepository.TryAcquire(chatId, Command, telegramId: telegramId))
        {
            return await messageAssistance.CommandNotReady(chatId, messageId, Command);
        }

        using var session = await db.OpenTransaction();
        session.StartTransaction();

        var isRemoved = await memberItemRepository.RemoveMemberItem(telegramId, chatId, MemberItemType.Box, session);

        if (!isRemoved)
        {
            return await Array(
                messageAssistance.SendNoItems(chatId),
                session.AbortTransactionAsync()).WhenAll();
        }

        var item = GetRandomItem();
        if (await memberItemRepository.AddMemberItem(telegramId, chatId, item, session))
        {
            Log.Information("Added item {0} to {1} in chat {2}", item, telegramId, chatId);
            await session.CommitTransactionAsync();
        }
        else
        {
            Log.Error("Failed to add item {0} to {1} in chat {2}", item, telegramId, chatId);
            return await Array(session.AbortTransactionAsync(),
                SendFailedToOpenBox(chatId, telegramId)).WhenAll();
        }

        return await botClient.GetChatMember(chatId, telegramId, cancelToken.Token)
            .ToTryAsync()
            .Map(member => member.GetUsername())
            .Match(username => messageAssistance.SendGetItemMessage(chatId, username, item),
                ex =>
                {
                    Log.Error(ex, "Failed to get chat member in chat {0} with telegramId {1}", chatId, telegramId);
                    return Task.FromResult(unit);
                });
    }

    public MemberItemType GetRandomItem()
    {
        var random = new Random();

        var itemChances = appConfig.ItemConfig.ItemChance;
        var total = itemChances.Values.Sum();
        var roll = random.NextDouble() * total;

        var cumulative = 0.0;

        foreach (var pair in itemChances)
        {
            cumulative += pair.Value;
            if (roll <= cumulative)
            {
                return pair.Key;
            }
        }

        return itemChances.Keys.First();
    }

    private async Task<Unit> SendFailedToOpenBox(long chatId, long telegramId) =>
        await botClient.SendMessageAndLog(chatId, appConfig.ItemConfig.FailedToOpenBoxMessage,
            message =>
            {
                Log.Information("Sent failed to open box message to {0} chat {1}", telegramId, chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send failed to open box message to {0} chat {1}", telegramId, chatId),
            cancelToken.Token);
}