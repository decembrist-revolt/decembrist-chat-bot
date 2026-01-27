using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Service.Buttons;
using JasperFx.Core;
using Lamar;
using Serilog;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class ListCommandHandler(
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    ExpiredMessageRepository expiredMessageRepository,
    CallbackRepository callbackRepository,
    ListService listService,
    BotClient botClient,
    AppConfig appConfig,
    CancellationTokenSource cancelToken,
    ListButtons listButtons,
    ChatConfigService chatConfigService) : ICommandHandler
{
    public const string CommandKey = "/list";
    public string Command => CommandKey;

    public string Description => appConfig.CommandAssistanceConfig.CommandDescriptions.GetValueOrDefault(CommandKey,
        "Shows a list of available content from the chat, options: " + _listOptions);

    public CommandLevel CommandLevel => CommandLevel.User;

    private readonly string _listOptions = string.Join(", ", Enum.GetValues<ListType>().Map(type => type.ToString()));

    [GeneratedRegex(@"\s+")]
    private static partial Regex ArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;
        var maybeListConfig = await chatConfigService.GetConfig(chatId, config => config.ListConfig);
        if (!maybeListConfig.TryGetSome(out var listConfig))
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);

        var taskResult = ParseText(text.Trim()).MatchAsync(
            listType => HandleList(chatId, telegramId, listType, listConfig), () =>
            {
                Log.Information("Failed to parse list type from text: {Text}", text);
                return SendHelpMessage(chatId, listConfig);
            });

        return await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private Option<ListType> ParseText(string text) =>
        Optional(text.Split(' ', 2))
            .Bind(parts => parts.Length > 1 ? Some(parts[1]) : None)
            .Filter(arg => arg.IsNotEmpty())
            .Map(arg => ArgsRegex().Replace(arg, " ").Trim())
            .Filter(arg => arg.Length > 0)
            .Bind(x => Enum.TryParse<ListType>(x, true, out var type) ? Some(type) : None);


    private async Task<Unit> HandleList(long chatId, long telegramId, ListType listType, ListConfig listConfig)
    {
        var isLock = await lockRepository.TryAcquire(chatId, Command, listType.ToString());
        if (!isLock)
        {
            return await messageAssistance.CommandNotReady(chatId, 0, Command);
        }

        var maybeKeysAndCount = await listService.GetListBody(chatId, listType);
        return await maybeKeysAndCount.MatchAsync(
            None: () => SendNotFound(chatId, listType, listConfig),
            Some: tuple => SendListSuccess(chatId, telegramId, listType, tuple.Item1, tuple.Item2, listConfig));
    }

    private Task<Unit> SendNotFound(long chatId, ListType listType, ListConfig listConfig)
    {
        var message = string.Format(listConfig.NotFound, listType);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private Task<Unit> SendListSuccess(long chatId, long telegramId, ListType listType, string keys, int totalCount,
        ListConfig listConfig)
    {
        var keyboard = listButtons.GetListChatMarkup(totalCount, listType);
        var message = string.Format(listConfig.SuccessTemplate, listType, totalCount, keys);
        Task? taskPermission = null;
        var taskSent = botClient.SendMessageAndLog(chatId, message, ParseMode.MarkdownV2,
            onSent: message => taskPermission = HandleOnSentSuccess(message, chatId, telegramId, listConfig),
            ex => Log.Error(ex, "Failed to send response to command: {0} from {1} to chat {2}",
                Command, nameof(SendListSuccess), chatId),
            replyMarkup: keyboard, cancelToken: cancelToken.Token
        );
        return Array(taskPermission.ToUnit() ?? Task.CompletedTask, taskSent).WhenAll();
    }

    private Task HandleOnSentSuccess(Message message, long chatId, long telegramId, ListConfig listConfig)
    {
        Log.Information(
            "Sent response to command:'{0}' from {1} to chat {2}", Command, nameof(HandleOnSentSuccess), chatId);

        var expireAt = DateTime.UtcNow.AddMinutes(listConfig.ExpirationMinutes);
        var id = new CallbackPermission.CompositeId(chatId, telegramId, CallbackType.List, message.MessageId);
        var permission = new CallbackPermission(id, expireAt);

        expiredMessageRepository.QueueMessage(chatId, message.MessageId, expireAt);
        return callbackRepository.AddCallbackPermission(permission);
    }

    private Task<Unit> SendHelpMessage(long chatId, ListConfig listConfig) => messageAssistance
        .SendCommandResponse(chatId, string.Format(listConfig.HelpMessage, Command, _listOptions), Command);
}

public enum ListType
{
    Lore,
    FastReply,
    Dust,
    Craft
}