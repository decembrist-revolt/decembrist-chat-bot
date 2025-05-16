using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Service.Buttons;
using Lamar;
using Serilog;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class LoreListCommandHandler(
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    LoreService loreService,
    BotClient botClient,
    AppConfig appConfig,
    CallbackRepository callbackRepository,
    LoreButtons loreButtons,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public const string CommandKey = "/lorelist";
    public string Command => CommandKey;
    public string Description => "Show a lore keys from the chat";
    public CommandLevel CommandLevel => CommandLevel.User;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var isLock = await lockRepository.TryAcquire(chatId, Command);
        var taskResult = isLock
            ? SendLoreList(chatId, telegramId)
            : messageAssistance.CommandNotReady(chatId, messageId, Command);

        return await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private async Task<Unit> SendLoreList(long chatId, long telegramId)
    {
        var maybeKeysAndCount = await loreService.GetLoreKeys(chatId);
        return await maybeKeysAndCount.MatchAsync(
            None: () => SendNotFound(chatId),
            Some: tuple => SendSuccess(chatId, telegramId, tuple.Item1, tuple.Item2));
    }

    private Task<Unit> SendNotFound(long chatId)
    {
        var message = appConfig.LoreListConfig.NotFound;
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }


    private Task<Unit> SendSuccess(long chatId, long telegramId, string keys, int totalCount)
    {
        var keyboard = loreButtons.GetLoreListChatMarkup(totalCount);
        var message = string.Format(appConfig.LoreListConfig.SuccessTemplate, totalCount, keys);
        Task? taskPermission = null;
        var taskSent = botClient.SendMessageAndLog(chatId, message, ParseMode.MarkdownV2,
            onSent: message => taskPermission = HandleOnSentSuccess(message, chatId, telegramId),
            ex => Log.Error(ex, "Failed to send response to command: {0} from {1} to chat {2}",
                Command, nameof(SendLoreList), chatId),
            replyMarkup: keyboard, cancelToken: cancelToken.Token
        );
        return Array(taskPermission.ToUnit() ?? Task.CompletedTask, taskSent).WhenAll();
    }

    private Task HandleOnSentSuccess(Message message, long chatId, long telegramId)
    {
        Log.Information("Sent response to command:'{0}' from {1} to chat {2}", Command, nameof(SendLoreList), chatId);

        var expireAt = DateTime.UtcNow.AddMinutes(appConfig.LoreListConfig.ExpirationMinutes);
        var id = new CallbackPermission.CompositeId(chatId, telegramId, CallbackType.LoreList, message.MessageId);
        var permission = new CallbackPermission(id, expireAt);

        expiredMessageRepository.QueueMessage(chatId, message.MessageId, expireAt);
        return callbackRepository.AddCallbackPermission(permission);
    }
}