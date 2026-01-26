using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class MinaHandler(
    BotClient botClient,
    MinaRepository minaRepository,
    CurseRepository curseRepository,
    ChatConfigService chatConfigService,
    CancellationTokenSource cancelToken)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return false;

        var maybeCurseConfig = await chatConfigService.GetConfig(chatId, config => config.CurseConfig);
        var maybeMinaConfig = await chatConfigService.GetConfig(chatId, config => config.MinaConfig);
        if (!maybeCurseConfig.TryGetSome(out var curseConfig) ||
            !maybeMinaConfig.TryGetSome(out var minaConfig)) return false;

        return await minaRepository.FindMineTrigger(chatId, text).MatchAsync(
            async trigger => await ActivateMine(trigger, messageId, telegramId, chatId, curseConfig, minaConfig),
            () => false);
    }

    private async Task<bool> ActivateMine(MineTrigger trigger, int messageId, long victimTelegramId, long chatId,
        CurseConfig2 curseConfig, MinaConfig2 minaConfig)
    {
        var expireAt = DateTime.UtcNow.AddMinutes(curseConfig.DurationMinutes);
        var curseMember = new ReactionSpamMember(new CompositeId(victimTelegramId, chatId), trigger.Emoji, expireAt);

        var result = await curseRepository.AddCurseMember(curseMember);
        if (result != CurseResult.Success) return false;

        await SetReaction(curseMember, messageId, chatId);
        await minaRepository.DeleteMineTrigger(trigger.Id);
        await SendMineActivationMessage(chatId, victimTelegramId, trigger, minaConfig);

        return true;
    }

    private async Task SendMineActivationMessage(long chatId, long victimTelegramId, MineTrigger trigger,
        MinaConfig2 minaConfig)
    {
        var victimUsername = await botClient.GetUsername(chatId, victimTelegramId, cancelToken.Token)
            .ToAsync()
            .IfNone(victimTelegramId.ToString);

        var ownerUsername = await botClient.GetUsername(chatId, trigger.Id.TelegramId, cancelToken.Token)
            .ToAsync()
            .IfNone(trigger.Id.TelegramId.ToString);

        var message = string.Format(minaConfig.ActivationMessage,
            victimUsername, trigger.Id.Trigger, trigger.Emoji.Emoji, ownerUsername);

        const string logTemplate = "Mine activation message {0} ChatId: {1} VictimId: {2} OwnerId: {3}";
        await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information(logTemplate, "sent", chatId, victimTelegramId, trigger.Id.TelegramId),
            ex => Log.Error(ex, logTemplate, "failed", chatId, victimTelegramId, trigger.Id.TelegramId),
            cancelToken.Token);
    }

    private async Task SetReaction(ReactionSpamMember member, int messageId, long chatId)
    {
        const string logTemplate = "Mine activated. Reaction {0} ChatId: {1} MessageId: {2} TelegramId: {3}";
        await botClient.SetReactionAndLog(chatId, messageId, [member.Emoji],
            _ => Log.Information(logTemplate, "added", chatId, messageId, member.Id.TelegramId),
            ex => Log.Error(ex, logTemplate, "failed", chatId, messageId, member.Id.TelegramId),
            cancelToken.Token);
    }
}