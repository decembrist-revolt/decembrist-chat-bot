using System.Text.RegularExpressions;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using JasperFx.Core;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public partial class LorCommandHandler(
    LorService lorService,
    LorRecordRepository lorRecordRepository,
    AppConfig appConfig,
    MessageAssistance messageAssistance) : ICommandHandler
{
    public string Command => "/lor";
    public string Description => "Show a lor page from the history chat";
    public CommandLevel CommandLevel => CommandLevel.User;

    [GeneratedRegex(@"\s+")]
    private static partial Regex ArgsRegex();

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
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
            .Map(arg => ArgsRegex().Replace(arg, " ").Trim())
            .Filter(arg => arg.Length > 0 && arg.Length <= appConfig.LorConfig.KeyLimit)
            .FilterAsync(arg => lorRecordRepository.IsLorRecordExist((chatId, arg)));
    }

    private async Task<Unit> SendLorRecord(long chatId, string key)
    {
        var message = await lorService.GetLorRecord((chatId, key));
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendNotFound(long chatId)
    {
        var message = appConfig.LorConfig.ChatLorNotFound;
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }
}