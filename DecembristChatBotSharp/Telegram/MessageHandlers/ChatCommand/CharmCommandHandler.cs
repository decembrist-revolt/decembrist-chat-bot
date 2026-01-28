using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using JasperFx.Core;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class CharmCommandHandler(
    CharmRepository charmRepository,
    AdminUserRepository adminUserRepository,
    ExpiredMessageRepository expiredMessageRepository,
    MessageAssistance messageAssistance,
    MemberItemService itemService,
    ChatConfigService chatConfigService,
    BotClient botClient,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public const string CommandKey = "/charm";
    public string Command => CommandKey;
    public string Description => "Mutes a user with a phrase, they can’t chat until the charm wears off";
    public CommandLevel CommandLevel => CommandLevel.Item;

    [GeneratedRegex(@"\s+")]
    private static partial Regex ArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var maybeCharmConfig = chatConfigService.GetConfig(parameters.ChatConfig, config => config.CharmConfig);
        if (!maybeCharmConfig.TryGetSome(out var charmConfig))
        {
            await messageAssistance.SendNotConfigured(chatId, messageId, Command);
            return chatConfigService.LogNonExistConfig(unit, nameof(CharmConfig), Command);
        }

        var taskResult = parameters.ReplyToTelegramId.MatchAsync(
            async receiverId => await HandleCharm(text, receiverId, chatId, telegramId, charmConfig),
            async () => await SendReceiverNotSet(chatId, charmConfig));

        return await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private async Task<Unit> HandleCharm(string text, long receiverId, long chatId, long telegramId,
        CharmConfig charmConfig)
    {
        var isAdmin = await adminUserRepository.IsAdmin((telegramId, chatId));
        if (isAdmin && text.Contains(ChatCommandHandler.DeleteSubcommand, StringComparison.OrdinalIgnoreCase))
        {
            var isDelete = await charmRepository.DeleteCharmMember((receiverId, chatId));
            return LogAssistant.LogDeleteResult(isDelete, receiverId, chatId, telegramId, Command);
        }

        if (receiverId == telegramId) return await SendSelfMessage(chatId, charmConfig);

        return await ParseText(text.Trim(), charmConfig).Match(
            None: async () => await SendHelpMessage(chatId, charmConfig),
            Some: async phrase =>
            {
                var expireAt = DateTime.UtcNow.AddMinutes(charmConfig.DurationMinutes);
                var charmMember = new CharmMember((receiverId, chatId), phrase, expireAt);

                var result = await itemService.UseCharm(chatId, telegramId, charmMember, isAdmin);
                return result switch
                {
                    CharmResult.NoItems => await messageAssistance.SendNoItems(chatId),
                    CharmResult.Duplicate => await SendDuplicateMessage(chatId, charmConfig),
                    CharmResult.Blocked => await messageAssistance.SendAmuletMessage(chatId, receiverId, Command),
                    CharmResult.Failed => await SendHelpMessage(chatId, charmConfig),
                    CharmResult.Success => await SendSuccessMessage(chatId, receiverId, phrase, charmConfig),
                    _ => unit
                };
            });
    }

    private Option<string> ParseText(string text, CharmConfig charmConfig)
    {
        var argsPosition = text.IndexOf(' ');
        return Optional(argsPosition != -1 ? text[(argsPosition + 1)..] : string.Empty)
            .Filter(arg => arg.IsNotEmpty())
            .Map(arg => ArgsRegex().Replace(arg, " ").Trim())
            .Filter(arg => arg.Length > 0 && arg.Length <= charmConfig.CharacterLimit);
    }

    private async Task<Unit> SendReceiverNotSet(long chatId, CharmConfig charmConfig)
    {
        var message = string.Format(charmConfig.ReceiverNotSetMessage, Command);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendHelpMessage(long chatId, CharmConfig charmConfig)
    {
        var message = string.Format(charmConfig.HelpMessage, Command, charmConfig.CharacterLimit);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendSelfMessage(long chatId, CharmConfig charmConfig)
    {
        var message = charmConfig.SelfMessage;
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendDuplicateMessage(long chatId, CharmConfig charmConfig)
    {
        var message = charmConfig.DuplicateMessage;
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendSuccessMessage(long chatId, long receiverId, string phrase, CharmConfig charmConfig)
    {
        var username = await botClient.GetUsername(chatId, receiverId, cancelToken.Token)
            .ToAsync()
            .IfNone(receiverId.ToString);
        var message = string.Format(charmConfig.SuccessMessage, username,
            charmConfig.DurationMinutes, phrase);
        const string logTemplate = "Charm success message sent {0} ChatId: {1}, Phrase:{2} Receiver: {3}";
        return await botClient.SendMessageAndLog(chatId, message,
            m =>
            {
                Log.Information(logTemplate, "success", chatId, phrase, receiverId);
                expiredMessageRepository.QueueMessage(chatId, m.MessageId,
                    DateTime.UtcNow.AddMinutes(charmConfig.DurationMinutes));
            },
            ex => Log.Error(ex, logTemplate, "failed", chatId, phrase, receiverId),
            cancelToken.Token);
    }
}