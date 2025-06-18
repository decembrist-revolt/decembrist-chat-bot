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
    OpenBoxService openBoxService,
    MessageAssistance messageAssistance,
    BotClient botClient,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public const string CommandKey = "/openbox";

    public string Command => CommandKey;
    public string Description => "Open surprise box if you have one";
    public CommandLevel CommandLevel => CommandLevel.User;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload) return unit;

        var (itemType, result) = await openBoxService.OpenBox(chatId, telegramId);
        result.LogOpenBoxResult(itemType, telegramId, chatId);

        var resultTask = result switch
        {
            OpenBoxResult.NoItems => messageAssistance.SendNoItems(chatId),
            OpenBoxResult.Failed => SendFailedToOpenBox(chatId, telegramId),
            OpenBoxResult.Success => SendBoxResult(itemType, chatId, telegramId),
            OpenBoxResult.SuccessX2 => SendBoxResult(itemType, chatId, telegramId, 2),
            OpenBoxResult.AmuletActivated => SendBoxResult(itemType, chatId, telegramId, 0),
            _ => throw new ArgumentOutOfRangeException()
        };
        return await Array(resultTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private Task<Unit> SendBoxResult(Option<MemberItemType> itemType, long chatId, long telegramId, int count = 1)
    {
        var maybeSend =
            from type in itemType.ToAsync()
            from username in botClient.GetUsername(chatId, telegramId, cancelToken.Token).ToAsync()
            select messageAssistance.SendGetItemMessage(chatId, username, type, count);

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