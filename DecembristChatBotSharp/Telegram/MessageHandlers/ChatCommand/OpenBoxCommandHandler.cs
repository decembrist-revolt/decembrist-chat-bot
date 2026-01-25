using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
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
    ChatConfigService chatConfigService,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public const string CommandKey = "/openbox";

    public string Command => CommandKey;

    public string Description =>
        appConfig.CommandConfig.CommandDescriptions.GetValueOrDefault(CommandKey, "Open surprise box if you have one");

    public CommandLevel CommandLevel => CommandLevel.User;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload) return unit;
        var maybeItemConfig = await chatConfigService.GetConfig(chatId, config => config.ItemConfig);
        if (!maybeItemConfig.TryGetSome(out var itemConfig))
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);

        var boxResult = await openBoxService.OpenBox(chatId, telegramId);
        boxResult.Result.LogOpenBoxResult(boxResult.ItemType, telegramId, chatId);

        var resultTask = boxResult.Result switch
        {
            OpenBoxResult.NoItems => messageAssistance.SendNoItems(chatId),
            OpenBoxResult.Failed => SendFailedToOpenBox(chatId, telegramId, itemConfig),
            OpenBoxResult.Success or OpenBoxResult.SuccessUnique => SendBoxResult(boxResult.ItemType, chatId,
                telegramId, boxResult.Quantity),
            OpenBoxResult.SuccessX2 => SendBoxResult(boxResult.ItemType, chatId, telegramId, boxResult.Quantity),
            OpenBoxResult.AmuletActivated => SendBoxResult(boxResult.ItemType, chatId, telegramId, 0),
            _ => throw new ArgumentOutOfRangeException()
        };
        return await Array(resultTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private Task<Unit> SendBoxResult(Option<MemberItemType> itemType, long chatId, long telegramId, int count)
    {
        var maybeSend =
            from type in itemType.ToAsync()
            from username in botClient.GetUsername(chatId, telegramId, cancelToken.Token).ToAsync()
            select messageAssistance.SendGetItemMessage(chatId, username, type, count);

        return maybeSend.IfSome(identity);
    }

    private async Task<Unit> SendFailedToOpenBox(long chatId, long telegramId, ItemConfig2 itemConfig) =>
        await botClient.SendMessageAndLog(chatId, itemConfig.FailedToOpenBoxMessage,
            message =>
            {
                Log.Information("Sent failed to open box message to {0} chat {1}", telegramId, chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send failed to open box message to {0} chat {1}", telegramId, chatId),
            cancelToken.Token);
}