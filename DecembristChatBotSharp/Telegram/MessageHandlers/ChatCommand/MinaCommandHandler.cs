using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class MinaCommandHandler(
    BotClient botClient,
    AppConfig appConfig,
    AdminUserRepository adminUserRepository,
    MinaRepository minaRepository,
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    MemberItemService itemService,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public const string CommandKey = "/mina";

    private static readonly System.Collections.Generic.HashSet<string> Emojis =
    [
        "👍", "👎", "❤", "🔥", "🥰", "👏", "😁", "🤔", "🤯", "😱", "🤬", "😢", "🎉", "🤩", "🤮", "💩", "🙏", "👌", "🕊",
        "🤡", "🥱", "🥴", "😍", "🐳", "❤‍", "🌚", "🌭", "💯", "🤣", "⚡", "🍌", "🏆", "💔", "🤨", "😐", "🍓", "🍾", "💋",
        "🖕", "😈", "😴", "😭", "🤓", "👻", "👨", "‍💻", "👀", "🎃", "🙈", "😇", "😨", "🤝", "✍", "🤗", "🫡", "🎅",
        "🎄", "☃", "💅", "🤪", "🗿", "🆒", "💘", "🙉", "🦄", "😘", "💊", "🙊", "😎", "👾", "🤷‍♂", "🤷", "🤷‍♀", "😡"
    ];

    private static readonly string EmojisString = string.Join(", ", Emojis);
    public string Command => CommandKey;

    public string Description => "Set a mine that will curse anyone who writes the trigger phrase";
    public CommandLevel CommandLevel => CommandLevel.Item;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var taskResult = HandleMina(telegramId, chatId, text);

        return await Array(
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private async Task<Unit> HandleMina(long telegramId, long chatId, string text)
    {
        var isAdmin = await adminUserRepository.IsAdmin((telegramId, chatId));

        if (isAdmin && text.Contains(ChatCommandHandler.DeleteSubcommand, StringComparison.OrdinalIgnoreCase))
        {
            return await HandleDelete(telegramId, chatId, text);
        }

        return await ParseEmojiAndTrigger(text.Trim()).MatchAsync(
            None: async () => await SendHelpMessageWithLock(chatId),
            Some: async data =>
            {
                var (emoji, trigger) = data;
                var expireAt = DateTime.UtcNow.AddMinutes(appConfig.MinaConfig.DurationMinutes);
                var mineTrigger =
                    new MineTrigger(new MineTrigger.CompositeId(telegramId, chatId, trigger), emoji, expireAt);

                var result = await itemService.UseMina(chatId, telegramId, mineTrigger, isAdmin);
                return result switch
                {
                    MinaResult.NoItems => await messageAssistance.SendNoItems(chatId),
                    MinaResult.Failed => await SendHelpMessageWithLock(chatId),
                    MinaResult.Duplicate => await SendDuplicateMessage(chatId),
                    MinaResult.Success => await SendSuccessMessage(chatId, trigger, emoji.Emoji),
                    _ => unit
                };
            });
    }

    private async Task<Unit> HandleDelete(long telegramId, long chatId, string text)
    {
        var triggerText = text.Replace(ChatCommandHandler.DeleteSubcommand, "", StringComparison.OrdinalIgnoreCase)
            .Replace(CommandKey, "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (string.IsNullOrWhiteSpace(triggerText))
        {
            return await SendHelpMessageWithLock(chatId);
        }

        var compositeId = new MineTrigger.CompositeId(telegramId, chatId, triggerText);
        var isDelete = await minaRepository.DeleteMineTrigger(compositeId);
        return LogAssistant.LogDeleteResult(isDelete, telegramId, chatId, 0, Command);
    }

    private async Task<Unit> SendDuplicateMessage(long chatId)
    {
        var message = appConfig.MinaConfig.DuplicateMessage;
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendHelpMessageWithLock(long chatId)
    {
        if (!await lockRepository.TryAcquire(chatId, Command))
        {
            return await messageAssistance.SendCommandNotReady(chatId, Command);
        }

        var message = string.Format(appConfig.MinaConfig.HelpMessage, Command, EmojisString);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendSuccessMessage(long chatId, string trigger, string emoji)
    {
        var expireAt = appConfig.MinaConfig.DurationMinutes;
        var message = string.Format(appConfig.MinaConfig.SuccessMessage, trigger, emoji);
        const string logTemplate = "Mina message sent {0} ChatId: {1}, Emoji:{2} Trigger: {3}";
        return await botClient.SendMessageAndLog(chatId, message,
            m =>
            {
                Log.Information(logTemplate, "success", chatId, emoji, trigger);
                expiredMessageRepository.QueueMessage(chatId, m.MessageId, DateTime.UtcNow.AddMinutes(expireAt));
            },
            ex => Log.Error(ex, logTemplate, "failed", chatId, emoji, trigger),
            cancelToken.Token);
    }

    private Option<(ReactionTypeEmoji emoji, string trigger)> ParseEmojiAndTrigger(string text)
    {
        var argsPosition = text.IndexOf(' ');
        if (argsPosition == -1) return None;

        var emojiPart = text[(argsPosition + 1)..];
        var triggerPosition = emojiPart.IndexOf(' ');

        if (triggerPosition == -1) return None;

        var emoji = emojiPart[..triggerPosition].Trim();
        var trigger = emojiPart[(triggerPosition + 1)..].Trim();

        if (!Emojis.Contains(emoji)
            || string.IsNullOrWhiteSpace(trigger)
            || trigger.Length > appConfig.MinaConfig.TriggerMaxLength) return None;

        return (new ReactionTypeEmoji { Emoji = emoji }, trigger);
    }
}