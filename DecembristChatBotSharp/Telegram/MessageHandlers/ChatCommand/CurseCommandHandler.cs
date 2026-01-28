using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using JasperFx.Core;
using Lamar;
using Serilog;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class CurseCommandHandler(
    BotClient botClient,
    AppConfig appConfig,
    AdminUserRepository adminUserRepository,
    CurseRepository curseRepository,
    ChatConfigService chatConfigService,
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    MemberItemService itemService,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public const string CommandKey = "/curse";

    private static readonly System.Collections.Generic.HashSet<string> Emojis =
    [
        "👍", "👎", "❤", "🔥", "🥰", "👏", "😁", "🤔", "🤯", "😱", "🤬", "😢", "🎉", "🤩", "🤮", "💩", "🙏", "👌", "🕊",
        "🤡", "🥱", "🥴", "😍", "🐳", "❤‍", "🌚", "🌭", "💯", "🤣", "⚡", "🍌", "🏆", "💔", "🤨", "😐", "🍓", "🍾", "💋",
        "🖕", "😈", "😴", "😭", "🤓", "👻", "👨", "‍💻", "👀", "🎃", "🙈", "😇", "😨", "🤝", "✍", "🤗", "🫡", "🎅",
        "🎄", "☃", "💅", "🤪", "🗿", "🆒", "💘", "🙉", "🦄", "😘", "💊", "🙊", "😎", "👾", "🤷‍♂", "🤷", "🤷‍♀", "😡"
    ];

    private static readonly string EmojisString = string.Join(", ", Emojis);
    public string Command => CommandKey;

    public string Description => appConfig.CommandAssistanceConfig.CommandDescriptions.GetValueOrDefault(CommandKey,
        "All user messages will be cursed by certain emoji");

    public CommandLevel CommandLevel => CommandLevel.Item;

    [GeneratedRegex(@"\s+")]
    private static partial Regex ArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;
        var maybeCurseConfig = chatConfigService.GetConfig(parameters.ChatConfig, config => config.CurseConfig);
        if (!maybeCurseConfig.TryGetSome(out var curseConfig))
        {
            await messageAssistance.SendNotConfigured(chatId, messageId, Command);
            return chatConfigService.LogNonExistConfig(unit, nameof(CurseConfig), Command);
        }

        var taskResult = parameters.ReplyToTelegramId.Match(
            async receiverId => await HandleCurse(telegramId, chatId, receiverId, text, messageId, curseConfig),
            async () => await SendReceiverNotSet(chatId, curseConfig));

        return await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private async Task<Unit> HandleCurse(long telegramId, long chatId, long receiverId, string text,
        int messageId, CurseConfig curseConfig)
    {
        var isAdmin = await adminUserRepository.IsAdmin((telegramId, chatId));
        var compositeId = (receiverId, chatId);
        if (isAdmin && text.Contains(ChatCommandHandler.DeleteSubcommand, StringComparison.OrdinalIgnoreCase))
        {
            var isDelete = await curseRepository.DeleteCurseMember(compositeId);
            return LogAssistant.LogDeleteResult(isDelete, telegramId, chatId, receiverId, Command);
        }

        return await ParseEmoji(text.Trim()).MatchAsync(
            None: async () => await SendHelpMessageWithLock(chatId, curseConfig),
            Some: async emoji =>
            {
                var expireAt = DateTime.UtcNow.AddMinutes(curseConfig.DurationMinutes);
                var reactMember = new ReactionSpamMember(compositeId, emoji, expireAt);

                var result = await itemService.UseCurse(chatId, telegramId, reactMember, isAdmin);
                return result switch
                {
                    CurseResult.NoItems => await messageAssistance.SendNoItems(chatId),
                    CurseResult.Failed => await SendHelpMessageWithLock(chatId, curseConfig),
                    CurseResult.Blocked => await messageAssistance.SendAmuletMessage(chatId, receiverId, Command),
                    CurseResult.Duplicate => await SendDuplicateMessage(chatId, curseConfig),
                    CurseResult.Success => await SendSuccessMessage(compositeId, emoji.Emoji, curseConfig),
                    _ => unit
                };
            });
    }

    private async Task<Unit> SendReceiverNotSet(long chatId, CurseConfig curseConfig)
    {
        var message = string.Format(curseConfig.ReceiverNotSetMessage, Command);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendDuplicateMessage(long chatId, CurseConfig curseConfig)
    {
        var message = curseConfig.DuplicateMessage;
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendHelpMessageWithLock(long chatId, CurseConfig curseConfig)
    {
        if (!await lockRepository.TryAcquire(chatId, Command))
        {
            return await messageAssistance.SendCommandNotReady(chatId, Command);
        }

        var message = string.Format(curseConfig.HelpMessage, Command, EmojisString);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendSuccessMessage(CompositeId id, string emoji, CurseConfig curseConfig)
    {
        var (receiverId, chatId) = id;
        var username = await botClient.GetUsername(chatId, receiverId, cancelToken.Token)
            .ToAsync()
            .IfNone(receiverId.ToString);
        var expireAt = curseConfig.DurationMinutes;
        var message = string.Format(curseConfig.SuccessMessage, username, emoji);
        const string logTemplate = "Curse message sent {0} ChatId: {1}, Emoji:{2} Receiver: {3}";
        return await botClient.SendMessageAndLog(chatId, message,
            m =>
            {
                Log.Information(logTemplate, "success", chatId, emoji, receiverId);
                expiredMessageRepository.QueueMessage(chatId, m.MessageId, DateTime.UtcNow.AddMinutes(expireAt));
            },
            ex => Log.Error(ex, logTemplate, "failed", chatId, emoji, receiverId),
            cancelToken.Token);
    }

    private Option<ReactionTypeEmoji> ParseEmoji(string text)
    {
        var argsPosition = text.IndexOf(' ');
        return Optional(argsPosition != -1 ? text[(argsPosition + 1)..] : string.Empty)
            .Filter(arg => arg.IsNotEmpty())
            .Map(arg => ArgsRegex().Replace(arg, " ").Trim())
            .Filter(Emojis.Contains)
            .Map(arg => new ReactionTypeEmoji { Emoji = arg });
    }
}