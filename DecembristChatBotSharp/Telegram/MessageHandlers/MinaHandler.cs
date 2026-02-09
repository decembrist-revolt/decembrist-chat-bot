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
    MinionService minionService,
    MessageAssistance messageAssistance,
    ChatConfigService chatConfigService,
    CancellationTokenSource cancelToken)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return false;

        var maybeCurseConfig = await chatConfigService.GetConfig(chatId, config => config.CurseConfig);
        var maybeMinaConfig = await chatConfigService.GetConfig(chatId, config => config.MinaConfig);
        if (!maybeCurseConfig.TryGetSome(out var curseConfig))
        {
            return chatConfigService.LogNonExistConfig(false, nameof(CurseConfig), nameof(MinaHandler));
        }

        if (!maybeMinaConfig.TryGetSome(out var minaConfig))
        {
            return chatConfigService.LogNonExistConfig(false, nameof(MinaConfig), nameof(MinaHandler));
        }

        return await minaRepository.FindMineTrigger(chatId, text).MatchAsync(
            async trigger => await ActivateMine(trigger, messageId, telegramId, chatId, curseConfig, minaConfig),
            () => false);
    }

    private async Task<bool> ActivateMine(MineTrigger trigger, int messageId, long victimTelegramId, long chatId,
        CurseConfig curseConfig, MinaConfig minaConfig)
    {
        var redirectTarget = await minionService.GetRedirectTarget(victimTelegramId, chatId);
        if (redirectTarget.TryGetSome(out var redirectedId))
        {
            var originalVictimId = victimTelegramId;
            victimTelegramId = redirectedId;
            await minionService.SendNegativeEffectRedirectMessage(chatId, originalVictimId, victimTelegramId);
        }

        var expireAt = DateTime.UtcNow.AddMinutes(curseConfig.DurationMinutes);
        var curseMember = new ReactionSpamMember(new CompositeId(victimTelegramId, chatId), trigger.Emoji, expireAt);

        var result = await curseRepository.AddCurseMember(curseMember);
        if (result != CurseResult.Success) return false;

        await SetReaction(curseMember, messageId, chatId);
        await minaRepository.DeleteMineTrigger(trigger.Id);
        await SendMineActivationMessage(chatId, victimTelegramId, trigger, minaConfig, curseConfig);

        return true;
    }

    private async Task<Unit> SendMineActivationMessage(long chatId, long victimTelegramId, MineTrigger trigger,
        MinaConfig minaConfig, CurseConfig curseConfig)
    {
        var victimUsername = await botClient.GetUsernameOrId(victimTelegramId, chatId, cancelToken.Token);
        var ownerUsername = await botClient.GetUsernameOrId(trigger.Id.TelegramId, chatId, cancelToken.Token);

        var message = string.Format(minaConfig.ActivationMessage,
            victimUsername, trigger.Id.Trigger, trigger.Emoji.Emoji, ownerUsername);

        var expiration = DateTime.UtcNow.AddMinutes(curseConfig.DurationMinutes);
        Log.Information("Mine activation message sending ChatId: {0} VictimId: {1} OwnerId: {2}", chatId,
            victimTelegramId, trigger.Id.TelegramId);
        return await messageAssistance.SendMessageExpired(chatId, message, nameof(MinaHandler), expiration);
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