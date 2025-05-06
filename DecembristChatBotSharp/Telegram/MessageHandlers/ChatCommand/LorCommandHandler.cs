using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class LorCommandHandler(
    LorService lorService,
    MessageAssistance messageAssistance) : ICommandHandler
{
    public string Command => "/lor";
    public string Description => "Show a lor page from the history chat";
    public CommandLevel CommandLevel => CommandLevel.Item;


    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;
        var taskResult = SendLorRecord(chatId, "OK");

        return await Array(messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private async Task<Unit> SendLorRecord(long chatId, string message)
    {
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendNotFound(long chatId, string key)
    {
        var message = "not found";
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }
}