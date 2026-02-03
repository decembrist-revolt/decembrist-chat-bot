using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
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
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    MemberItemService itemService,
    MinionService minionService,
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

    public string Description =>
        appConfig.CommandConfig.CommandDescriptions.GetValueOrDefault(CommandKey,
            "All user messages will be cursed by certain emoji");

    public CommandLevel CommandLevel => CommandLevel.Item;

    [GeneratedRegex(@"\s+")]
    private static partial Regex ArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var taskResult = parameters.ReplyToTelegramId.Match(
            async receiverId => await HandleCurse(telegramId, chatId, receiverId, text, messageId),
            async () => await SendReceiverNotSet(chatId));

        return await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private async Task<Unit> HandleCurse(long telegramId, long chatId, long receiverId, string text,
        int messageId)
    {
        var isAdmin = await adminUserRepository.IsAdmin((telegramId, chatId));

        var redirectTarget = await minionService.GetRedirectTarget(receiverId, chatId);
        var originalReceiverId = receiverId;
        var isRedirected = redirectTarget.TryGetSome(out var redirectedId);
        if (isRedirected) receiverId = redirectedId;

        var compositeId = (receiverId, chatId);

        if (isAdmin && text.Contains(ChatCommandHandler.DeleteSubcommand, StringComparison.OrdinalIgnoreCase))
        {
            var isDelete = await curseRepository.DeleteCurseMember(compositeId);
            return LogAssistant.LogDeleteResult(isDelete, telegramId, chatId, receiverId, Command);
        }

        return await ParseEmoji(text.Trim()).MatchAsync(
            None: async () => await SendHelpMessageWithLock(chatId),
            Some: async emoji =>
            {
                var expireAt = DateTime.UtcNow.AddMinutes(appConfig.CurseConfig.DurationMinutes);
                var reactMember = new ReactionSpamMember(compositeId, emoji, expireAt);

                var result = await itemService.UseCurse(chatId, telegramId, reactMember, isAdmin);
                return result switch
                {
                    CurseResult.NoItems => await messageAssistance.SendNoItems(chatId),
                    CurseResult.Failed => await SendHelpMessageWithLock(chatId),
                    CurseResult.Blocked when isRedirected =>
                        await SendAmuletRedirected(compositeId, originalReceiverId),
                    CurseResult.Blocked => await messageAssistance.SendAmuletMessage(chatId, receiverId, Command),
                    CurseResult.Duplicate when isRedirected => await SendDuplicateRedirectedMessage(chatId),
                    CurseResult.Duplicate => await SendDuplicateMessage(chatId),
                    CurseResult.Success when isRedirected =>
                        await SendSuccessRedirectMessage(compositeId, originalReceiverId, emoji.Emoji),
                    CurseResult.Success => await SendSuccessMessage(compositeId, emoji.Emoji),
                    _ => unit
                };
            });
    }

    private async Task<Unit> SendReceiverNotSet(long chatId)
    {
        var message = string.Format(appConfig.CurseConfig.ReceiverNotSetMessage, Command);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendDuplicateMessage(long chatId)
    {
        var message = appConfig.CurseConfig.DuplicateMessage;
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendDuplicateRedirectedMessage(long chatId)
    {
        return await messageAssistance.SendCommandResponse(chatId,
            "Миньон этого пользователя уже проклят, попробуйте позже", Command);
    }

    private async Task<Unit> SendHelpMessageWithLock(long chatId)
    {
        if (!await lockRepository.TryAcquire(chatId, Command))
        {
            return await messageAssistance.SendCommandNotReady(chatId, Command);
        }

        var message = string.Format(appConfig.CurseConfig.HelpMessage, Command, EmojisString);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendSuccessMessage(CompositeId id, string emoji)
    {
        var (receiverId, chatId) = id;
        var username = await botClient.GetUsernameOrId(receiverId, chatId, cancelToken.Token);
        var expireAt = appConfig.CurseConfig.DurationMinutes;
        var message = string.Format(appConfig.CurseConfig.SuccessMessage, username, emoji);
        Log.Information("Curse message sent ChatId: {chatId}, Emoji:{emoji} Receiver: {receiver}", chatId, emoji,
            receiverId);
        return await messageAssistance.SendCommandResponse(chatId, message, Command,
            DateTime.UtcNow.AddMinutes(expireAt));
    }

    private async Task<Unit> SendSuccessRedirectMessage(CompositeId id, long originalReceiverId, string emoji)
    {
        var (receiverId, chatId) = id;
        var username = await botClient.GetUsernameOrId(receiverId, chatId, cancelToken.Token);
        var message = string.Format(appConfig.CurseConfig.SuccessMessage, username, emoji);
        var exp = DateTime.UtcNow.AddMinutes(appConfig.CharmConfig.DurationMinutes);
        Log.Information("Curse redirected ChatId: {0}, Phrase:{1} Receiver: {2}", chatId, emoji, receiverId);
        await minionService.SendNegativeEffectRedirectMessage(chatId, originalReceiverId, receiverId);
        return await messageAssistance.SendCommandResponse(chatId, message, Command, exp);
    }

    private async Task<Unit> SendAmuletRedirected(CompositeId id, long originalReceiverId)
    {
        var (receiverId, chatId) = id;
        await minionService.SendNegativeEffectRedirectMessage(chatId, originalReceiverId, receiverId);
        return await messageAssistance.SendAmuletMessage(chatId, receiverId, Command);
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