using System.Text.RegularExpressions;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using JasperFx.Core;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class LoreCommandHandler(
    LoreService loreService,
    LoreRecordRepository loreRecordRepository,
    AppConfig appConfig,
    MessageAssistance messageAssistance) : ICommandHandler
{
    public string Command => "/lore";
    public string Description => "Show a lore record from the lore chat";
    public CommandLevel CommandLevel => CommandLevel.User;

    [GeneratedRegex(@"\s+")]
    private static partial Regex ArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, _, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;
        var maybeKey = ParseText(text.Trim(), chatId);
        var taskResult = maybeKey
            .MapAsync(key => SendLorRecord(chatId, key))
            .IfNoneAsync(() => SendNotFound(chatId));

        return await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private OptionAsync<string> ParseText(string text, long chatId)
    {
        var argsPosition = text.IndexOf(' ');
        return Optional(argsPosition != -1 ? text[(argsPosition + 1)..] : string.Empty)
            .Filter(arg => arg.IsNotEmpty())
            .Map(arg => ArgsRegex().Replace(arg, " ").Trim().ToLowerInvariant())
            .Filter(arg => arg.Length > 0 && arg.Length <= appConfig.LoreConfig.KeyLimit)
            .FilterAsync(arg => loreRecordRepository.IsLoreRecordExist((chatId, arg)));
    }

    private async Task<Unit> SendLorRecord(long chatId, string key)
    {
        var expireAt = DateTime.UtcNow.AddMinutes(appConfig.LoreConfig.ChatLoreExpiration);
        var message = await loreService.GetLoreRecord(chatId, key);
        return await messageAssistance.SendCommandResponse(chatId, message, Command, expireAt);
    }

    private Task<Unit> SendNotFound(long chatId)
    {
        var message = string.Format(appConfig.LoreConfig.LoreNotFound, Command);
        return messageAssistance.SendCommandResponse(chatId, message, Command);
    }
}