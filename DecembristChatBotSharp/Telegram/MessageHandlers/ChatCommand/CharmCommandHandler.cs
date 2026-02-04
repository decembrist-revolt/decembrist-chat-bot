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
    MessageAssistance messageAssistance,
    MemberItemService itemService,
    MinionService minionService,
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

        var maybeCharmConfig = await chatConfigService.GetConfig(chatId, config => config.CharmConfig);
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
                var redirectTarget = await minionService.GetRedirectTarget(receiverId, chatId);
                var originalReceiverId = receiverId;
                var isRedirected = redirectTarget.TryGetSome(out var redirectedId);
                if (isRedirected) receiverId = redirectedId;

                var expireAt = DateTime.UtcNow.AddMinutes(charmConfig.DurationMinutes);
                var charmMember = new CharmMember((receiverId, chatId), phrase, expireAt);

                var result = await itemService.UseCharm(chatId, telegramId, charmMember, isAdmin);
                return result switch
                {
                    CharmResult.NoItems => await messageAssistance.SendNoItems(chatId),
                    CharmResult.Duplicate when isRedirected => await SendDuplicateRedirectedMessage(chatId),
                    CharmResult.Duplicate => await SendDuplicateMessage(chatId, charmConfig),
                    CharmResult.Blocked when isRedirected => await SendAmuletRedirected(chatId, receiverId,
                        originalReceiverId),
                    CharmResult.Blocked => await messageAssistance.SendAmuletMessage(chatId, receiverId, Command),
                    CharmResult.Failed => await SendHelpMessage(chatId, charmConfig),
                    CharmResult.Success when isRedirected => await SendSuccessRedirectMessage(chatId, receiverId,
                        originalReceiverId, phrase, charmConfig),
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

    private async Task<Unit> SendDuplicateRedirectedMessage(long chatId)
    {
        return await messageAssistance.SendCommandResponse(chatId,
            "Миньон этого пользователя уже зачарован, попробуйте позже", Command);
    }

    private async Task<Unit> SendSuccessMessage(long chatId, long receiverId, string phrase, CharmConfig charmConfig)
    {
        var username = await botClient.GetUsernameOrId(receiverId, chatId, cancelToken.Token);
        var message = string.Format(charmConfig.SuccessMessage, username, charmConfig.DurationMinutes, phrase);
        var exp = DateTime.UtcNow.AddMinutes(charmConfig.DurationMinutes);
        Log.Information("Charm message sent ChatId: {0}, Phrase:{1} Receiver: {2}", chatId, phrase, receiverId);
        return await messageAssistance.SendCommandResponse(chatId, message, Command, exp);
    }

    private async Task<Unit> SendSuccessRedirectMessage(long chatId, long receiverId, long originalReceiverId,
        string phrase, CharmConfig charmConfig)
    {
        var username = await botClient.GetUsernameOrId(receiverId, chatId, cancelToken.Token);
        var message = string.Format(charmConfig.SuccessMessage, username, charmConfig.DurationMinutes, phrase);
        var exp = DateTime.UtcNow.AddMinutes(charmConfig.DurationMinutes);
        Log.Information("Charm redirected ChatId: {0}, Phrase:{1} Receiver: {2}", chatId, phrase, receiverId);
        await minionService.SendNegativeEffectRedirectMessage(chatId, originalReceiverId, receiverId);
        return await messageAssistance.SendCommandResponse(chatId, message, Command, exp);
    }

    private async Task<Unit> SendAmuletRedirected(long chatId, long receiverId, long originalReceiverId)
    {
        await minionService.SendNegativeEffectRedirectMessage(chatId, originalReceiverId, receiverId);
        return await messageAssistance.SendAmuletMessage(chatId, receiverId, Command);
    }
}