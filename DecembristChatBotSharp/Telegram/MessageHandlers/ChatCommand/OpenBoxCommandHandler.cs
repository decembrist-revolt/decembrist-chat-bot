using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class OpenBoxCommandHandler(
    AppConfig appConfig,
    ExpiredMessageRepository expiredMessageRepository,
    MemberItemService memberItemService,
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    BotClient botClient,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public const string CommandKey = "/openbox";

    public string Command => CommandKey;
    public string Description => "Open surprise box if you have one";
    public CommandLevel CommandLevel => CommandLevel.User;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var chatId = parameters.ChatId;
        var telegramId = parameters.TelegramId;
        var messageId = parameters.MessageId;
        if (parameters.Payload is not TextPayload) return unit;

        if (await adminUserRepository.IsAdmin(new(telegramId, chatId)))
        {
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
        }

        if (!await lockRepository.TryAcquire(chatId, Command, telegramId: telegramId))
        {
            return await messageAssistance.CommandNotReady(chatId, messageId, Command);
        }

        var (itemType, result) = await memberItemService.OpenBox(chatId, telegramId);

        return result switch
        {
            OpenBoxResult.NoItems => await messageAssistance.SendNoItems(chatId),
            OpenBoxResult.Failed => await SendFailedToOpenBox(chatId, telegramId),
            OpenBoxResult.Success => await SendBoxResult(itemType, chatId, telegramId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private Task<Unit> SendBoxResult(Option<MemberItemType> itemType, long chatId, long telegramId)
    {
        var maybeSend =
            from type in itemType.ToAsync()
            from username in botClient.GetUsername(chatId, telegramId, cancelToken.Token).ToAsync()
            select messageAssistance.SendGetItemMessage(chatId, username, type);

        return maybeSend.IfSome(identity);
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