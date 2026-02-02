using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
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
    MinionService minionService,
    AppConfig appConfig,
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

        var taskResult = parameters.ReplyToTelegramId.MatchAsync(
            async receiverId => await HandleCharm(text, receiverId, chatId, telegramId),
            async () => await SendReceiverNotSet(chatId));

        return await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private async Task<Unit> HandleCharm(string text, long receiverId, long chatId, long telegramId)
    {
        var isAdmin = await adminUserRepository.IsAdmin((telegramId, chatId));
        if (isAdmin && text.Contains(ChatCommandHandler.DeleteSubcommand, StringComparison.OrdinalIgnoreCase))
        {
            var isDelete = await charmRepository.DeleteCharmMember((receiverId, chatId));
            return LogAssistant.LogDeleteResult(isDelete, receiverId, chatId, telegramId, Command);
        }

        if (receiverId == telegramId) return await SendSelfMessage(chatId);


        return await ParseText(text.Trim()).Match(
            None: async () => await SendHelpMessage(chatId),
            Some: async phrase =>
            {
                var redirectTarget = await minionService.GetRedirectTarget(receiverId, chatId);
                var originalReceiverId = receiverId;
                var isRedirected = redirectTarget.TryGetSome(out receiverId);

                var expireAt = DateTime.UtcNow.AddMinutes(appConfig.CharmConfig.DurationMinutes);
                var charmMember = new CharmMember((receiverId, chatId), phrase, expireAt);

                var result = await itemService.UseCharm(chatId, telegramId, charmMember, isAdmin);
                return result switch
                {
                    CharmResult.NoItems => await messageAssistance.SendNoItems(chatId),
                    CharmResult.Duplicate => await SendDuplicateMessage(chatId),
                    CharmResult.Blocked when isRedirected => await messageAssistance.SendAmuletMessage(chatId,
                        receiverId, Command),
                    CharmResult.Blocked => await messageAssistance.SendAmuletMessage(chatId, receiverId, Command),
                    CharmResult.Failed => await SendHelpMessage(chatId),
                    CharmResult.Success when isRedirected => await SendSuccessWithRedirectMessage(chatId, receiverId,
                        originalReceiverId, phrase),
                    CharmResult.Success => await SendSuccessMessage(chatId, receiverId, phrase),
                    _ => unit
                };
            });
    }

    private Option<string> ParseText(string text)
    {
        var argsPosition = text.IndexOf(' ');
        return Optional(argsPosition != -1 ? text[(argsPosition + 1)..] : string.Empty)
            .Filter(arg => arg.IsNotEmpty())
            .Map(arg => ArgsRegex().Replace(arg, " ").Trim())
            .Filter(arg => arg.Length > 0 && arg.Length <= appConfig.CharmConfig.CharacterLimit);
    }

    private async Task<Unit> SendReceiverNotSet(long chatId)
    {
        var message = string.Format(appConfig.CharmConfig.ReceiverNotSetMessage, Command);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendHelpMessage(long chatId)
    {
        var message = string.Format(appConfig.CharmConfig.HelpMessage, Command, appConfig.CharmConfig.CharacterLimit);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendSelfMessage(long chatId)
    {
        var message = appConfig.CharmConfig.SelfMessage;
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendDuplicateMessage(long chatId)
    {
        var message = appConfig.CharmConfig.DuplicateMessage;
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendSuccessMessage(long chatId, long receiverId, string phrase)
    {
        var username = await botClient.GetUsernameOrId(receiverId, chatId, cancelToken.Token);
        var message = string.Format(appConfig.CharmConfig.SuccessMessage, username,
            appConfig.CharmConfig.DurationMinutes, phrase);
        var exp = DateTime.UtcNow.AddMinutes(appConfig.CharmConfig.DurationMinutes);
        Log.Information("Charm message sent ChatId: {0}, Phrase:{1} Receiver: {2}", chatId, phrase, receiverId);
        return await messageAssistance.SendCommandResponse(chatId, message, Command, exp);
    }

    private async Task<Unit> SendSuccessWithRedirectMessage(long chatId, long receiverId, long originalReceiverId,
        string phrase)
    {
        var username = await botClient.GetUsernameOrId(receiverId, chatId, cancelToken.Token);
        var message = string.Format(appConfig.CharmConfig.SuccessMessage, username,
            appConfig.CharmConfig.DurationMinutes, phrase);
        var exp = DateTime.UtcNow.AddMinutes(appConfig.CharmConfig.DurationMinutes);
        Log.Information("Charm message sent ChatId: {0}, Phrase:{1} Receiver: {2}", chatId, phrase, receiverId);
        await minionService.SendNegativeEffectRedirectMessage(chatId, originalReceiverId, receiverId);
        return await messageAssistance.SendCommandResponse(chatId, message, Command, exp);
    }

    private async Task<Unit> SendRedirected(long chatId, long receiverId, long originalReceiverId,
        string phrase)
    {
        var username = await botClient.GetUsernameOrId(receiverId, chatId, cancelToken.Token);
        var message = string.Format(appConfig.CharmConfig.SuccessMessage, username,
            appConfig.CharmConfig.DurationMinutes, phrase);
        var exp = DateTime.UtcNow.AddMinutes(appConfig.CharmConfig.DurationMinutes);
        Log.Information("Charm message sent ChatId: {0}, Phrase:{1} Receiver: {2}", chatId, phrase, receiverId);
        await minionService.SendNegativeEffectRedirectMessage(chatId, originalReceiverId, receiverId);
        return await messageAssistance.SendCommandResponse(chatId, message, Command, exp);
    }
}