using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using JasperFx.Core;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class LoreCommandHandler(
    AppConfig appConfig,
    LoreService loreService,
    LoreRecordRepository loreRecordRepository,
    MessageAssistance messageAssistance,
    ChatConfigService chatConfigService) : ICommandHandler
{
    public string Command => "/lore";

    public string Description =>
        appConfig.CommandConfig.CommandDescriptions.GetValueOrDefault(Command, "Show a lore record from the lore chat");

    public CommandLevel CommandLevel => CommandLevel.User;

    [GeneratedRegex(@"\s+")]
    private static partial Regex ArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, _, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;
        var maybeLoreConfig = await chatConfigService.GetConfig(chatId, config => config.LoreConfig);
        if (!maybeLoreConfig.TryGetSome(out var loreConfig))
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);

        var maybeKey = ParseText(text.Trim(), chatId, loreConfig);
        var taskResult = maybeKey
            .MapAsync(key => SendLorRecord(chatId, key, loreConfig))
            .IfNoneAsync(() => SendNotFound(chatId, loreConfig));

        return await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private OptionAsync<string> ParseText(string text, long chatId, LoreConfig2 loreConfig)
    {
        var argsPosition = text.IndexOf(' ');
        return Optional(argsPosition != -1 ? text[(argsPosition + 1)..] : string.Empty)
            .Filter(arg => arg.IsNotEmpty())
            .Map(arg => ArgsRegex().Replace(arg, " ").Trim().ToLowerInvariant())
            .Filter(arg => arg.Length > 0 && arg.Length <= loreConfig.KeyLimit)
            .FilterAsync(arg => loreRecordRepository.IsLoreRecordExist((chatId, arg)));
    }

    private async Task<Unit> SendLorRecord(long chatId, string key, LoreConfig2 loreConfig)
    {
        var expireAt = DateTime.UtcNow.AddMinutes(loreConfig.ChatLoreExpiration);
        var message = await loreService.GetLoreRecord(chatId, key);
        return await messageAssistance.SendCommandResponse(chatId, message, Command, expireAt);
    }

    private Task<Unit> SendNotFound(long chatId, LoreConfig2 loreConfig)
    {
        var message = string.Format(loreConfig.LoreNotFound, Command,
            $"{ListCommandHandler.CommandKey} {ListType.Lore}");
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }
}