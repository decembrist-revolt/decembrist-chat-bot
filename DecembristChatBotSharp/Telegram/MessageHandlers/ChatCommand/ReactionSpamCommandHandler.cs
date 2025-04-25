﻿using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using JasperFx.Core;
using Lamar;
using Serilog;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class ReactionSpamCommandHandler(
    BotClient botClient,
    AppConfig appConfig,
    AdminUserRepository adminUserRepository,
    ReactionSpamRepository reactionSpamRepository,
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

    public string Description => "All user messages will be cursed by certain emoji";
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
        var compositeId = (receiverId, chatId);
        if (isAdmin && text.Contains(ChatCommandHandler.DeleteSubcommand, StringComparison.OrdinalIgnoreCase))
        {
            var isDelete = await reactionSpamRepository.DeleteReactionSpamMember(compositeId);
            return LogAssistant.LogDeleteResult(isDelete, telegramId, chatId, receiverId, Command);
        }

        return await ParseEmoji(text.Trim()).MatchAsync(
            None: async () => await SendHelpMessageWithLock(chatId, messageId),
            Some: async emoji =>
            {
                var expireAt = DateTime.UtcNow.AddMinutes(appConfig.ReactionSpamConfig.DurationMinutes);
                var reactMember = new ReactionSpamMember(compositeId, emoji, expireAt);

                var result = await itemService.UseReactionSpam(chatId, telegramId, reactMember, isAdmin);
                return result switch
                {
                    ReactionSpamResult.NoItems => await messageAssistance.SendNoItems(chatId),
                    ReactionSpamResult.Failed => await SendHelpMessageWithLock(chatId, messageId),
                    ReactionSpamResult.Duplicate => await SendDuplicateMessage(chatId),
                    ReactionSpamResult.Success => await SendSuccessMessage(compositeId, emoji.Emoji),
                    _ => unit
                };
            });
    }

    private async Task<Unit> SendReceiverNotSet(long chatId)
    {
        var message = string.Format(appConfig.ReactionSpamConfig.ReceiverNotSetMessage, Command);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendDuplicateMessage(long chatId)
    {
        var message = appConfig.ReactionSpamConfig.DuplicateMessage;
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendHelpMessageWithLock(long chatId, int messageId)
    {
        if (!await lockRepository.TryAcquire(chatId, Command))
        {
            return await messageAssistance.SendCommandNotReady(chatId, Command);
        }

        var message = string.Format(appConfig.ReactionSpamConfig.HelpMessage, Command, EmojisString);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendSuccessMessage(CompositeId id, string emoji)
    {
        var (receiverId, chatId) = id;
        var username = await botClient.GetUsername(chatId, receiverId, cancelToken.Token)
            .ToAsync()
            .IfNone(receiverId.ToString);
        var expireAt = appConfig.ReactionSpamConfig.DurationMinutes;
        var message = string.Format(appConfig.ReactionSpamConfig.SuccessMessage, username, emoji);
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