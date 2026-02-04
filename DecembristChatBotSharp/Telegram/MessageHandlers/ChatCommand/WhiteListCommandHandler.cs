using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class WhiteListCommandHandler(
    AppConfig appConfig,
    MessageAssistance messageAssistance,
    WhiteListRepository whiteListRepository) : ICommandHandler
{
    public string Command => "/whitelist";
    public string Description => appConfig.CommandAssistanceConfig.CommandDescriptions.GetValueOrDefault(Command, "Adding or removing from the white list");
    public CommandLevel CommandLevel => CommandLevel.Admin;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var taskResult = parameters.ReplyToTelegramId.Match(
            async receiverId => await HandleRestrict(text, receiverId, chatId, telegramId),
            () =>
            {
                Log.Warning("Reply user for {0} not set in chat {1}", Command, chatId);
                return Task.FromResult(unit);
            });

        return await Array(taskResult,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private async Task<Unit> HandleRestrict(string text, long receiverId, long chatId, long adminId)
    {
        var compositeId = new CompositeId(receiverId, chatId);
        var isDelete = text.Contains(ChatCommandHandler.DeleteSubcommand, StringComparison.OrdinalIgnoreCase);
        return isDelete
            ? await DeleteWhiteListAndLog(compositeId, adminId)
            : await AddWhiteListMemberAndLog(compositeId, adminId);
    }

    private async Task<Unit> AddWhiteListMemberAndLog(CompositeId id, long adminId)
    {
        var (telegramId, chatId) = id;

        if (await whiteListRepository.AddWhiteListMember(new WhiteListMember(id)))
        {
            Log.Information("Added whitelist member: {0} in chat {1} by {2}", telegramId, chatId, adminId);
        }
        else
        {
            Log.Error("WhiteList member: {0} not added in chat {1} by {2}", telegramId, chatId, adminId);
        }

        return unit;
    }

    private async Task<Unit> DeleteWhiteListAndLog(CompositeId id, long adminId)
    {
        var (telegramId, chatId) = id;
        var isDelete = await whiteListRepository.DeleteWhiteListMember(id);
        LogAssistant.LogDeleteResult(isDelete, adminId, chatId, telegramId, Command);
        return unit;
    }
}