using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
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
    ChatConfigService chatConfigService,
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

    public string Description => appConfig.CommandConfig.CommandDescriptions.GetValueOrDefault(CommandKey,
        "Set a mine that will curse anyone who writes the trigger phrase");

    public CommandLevel CommandLevel => CommandLevel.Item;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;
        var maybeMinaConfig = await chatConfigService.GetConfig(chatId, config => config.MinaConfig);
        if (!maybeMinaConfig.TryGetSome(out var minaConfig))
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);

        var taskResult = HandleMina(telegramId, chatId, text, minaConfig);

        return await Array(
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private async Task<Unit> HandleMina(long telegramId, long chatId, string text, MinaConfig2 minaConfig)
    {
        var isAdmin = await adminUserRepository.IsAdmin((telegramId, chatId));

        if (isAdmin && text.Contains(ChatCommandHandler.DeleteSubcommand, StringComparison.OrdinalIgnoreCase))
        {
            return await HandleDelete(telegramId, chatId, text, minaConfig);
        }

        return await ParseEmojiAndTrigger(text.Trim(), minaConfig).MatchAsync(
            None: async () => await SendHelpMessageWithLock(chatId, minaConfig),
            Some: async data =>
            {
                var (emoji, trigger) = data;
                var expireAt = DateTime.UtcNow.AddMinutes(minaConfig.DurationMinutes);
                var mineTrigger =
                    new MineTrigger(new MineTrigger.CompositeId(telegramId, chatId, trigger), emoji, expireAt);

                var result = await itemService.UseMina(chatId, telegramId, mineTrigger, isAdmin);
                return result switch
                {
                    MinaResult.NoItems => await messageAssistance.SendNoItems(chatId),
                    MinaResult.Failed => await SendHelpMessageWithLock(chatId, minaConfig),
                    MinaResult.Duplicate => await SendDuplicateMessage(chatId, minaConfig),
                    MinaResult.Success => await SendSuccessMessage(chatId, trigger, emoji.Emoji, minaConfig),
                    _ => unit
                };
            });
    }

    private async Task<Unit> HandleDelete(long telegramId, long chatId, string text, MinaConfig2 minaConfig)
    {
        var triggerText = text.Replace(ChatCommandHandler.DeleteSubcommand, "", StringComparison.OrdinalIgnoreCase)
            .Replace(CommandKey, "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (string.IsNullOrWhiteSpace(triggerText))
        {
            return await SendHelpMessageWithLock(chatId, minaConfig);
        }

        var compositeId = new MineTrigger.CompositeId(telegramId, chatId, triggerText);
        var isDelete = await minaRepository.DeleteMineTrigger(compositeId);
        return LogAssistant.LogDeleteResult(isDelete, telegramId, chatId, 0, Command);
    }

    private async Task<Unit> SendDuplicateMessage(long chatId, MinaConfig2 minaConfig)
    {
        var message = minaConfig.DuplicateMessage;
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendHelpMessageWithLock(long chatId, MinaConfig2 minaConfig)
    {
        if (!await lockRepository.TryAcquire(chatId, Command))
        {
            return await messageAssistance.SendCommandNotReady(chatId, Command);
        }

        var message = string.Format(minaConfig.HelpMessage, Command, EmojisString);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendSuccessMessage(long chatId, string trigger, string emoji, MinaConfig2 minaConfig)
    {
        var expireAt = minaConfig.DurationMinutes;
        var message = string.Format(minaConfig.SuccessMessage, trigger, emoji);
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

    private Option<(ReactionTypeEmoji emoji, string trigger)> ParseEmojiAndTrigger(string text, MinaConfig2 minaConfig)
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
            || trigger.Length > minaConfig.TriggerMaxLength) return None;

        return (new ReactionTypeEmoji { Emoji = emoji }, trigger);
    }
}