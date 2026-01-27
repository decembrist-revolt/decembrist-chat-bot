using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class MazeGameCommandHandler(
    BotClient botClient,
    MessageAssistance messageAssistance,
    AdminUserRepository adminUserRepository,
    MazeGameService mazeGameService,
    ChatConfigService chatConfigService,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public string Command => "/mazegame";
    public string Description => "Start a maze game (Admin only)";
    public CommandLevel CommandLevel => CommandLevel.Admin;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var isAdmin = await adminUserRepository.IsAdmin((telegramId, chatId));
        if (!isAdmin)
        {
            return await Array(
                messageAssistance.SendAdminOnlyMessage(chatId, telegramId),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)
            ).WhenAll();
        }

        if (text.Split(' ') is [_, ChatCommandHandler.DeleteSubcommand])
        {
            return await Array(RemoveGame(chatId),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)
            ).WhenAll();
        }

        var maybeMazeConfig = await chatConfigService.GetConfig(chatId, config => config.MazeConfig);
        if (!maybeMazeConfig.TryGetSome(out var mazeConfig))
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);

        var maybeCommandConfig = await chatConfigService.GetConfig(chatId, config => config.CommandConfig);
        if (!maybeCommandConfig.TryGetSome(out var commandConfig))
            return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);

        var url = await botClient.GetBotStartLink(
            PrivateMessageHandler.GetCommandForChat(PrivateMessageHandler.MazeGameCommandSuffix, chatId));
        var replyMarkup = new InlineKeyboardMarkup(
            InlineKeyboardButton.WithUrl(commandConfig.InviteToDirectMessage, url));
        var message = mazeConfig.AnnouncementMessage;

        var existingGame = await mazeGameService.FindActiveGameForChat(chatId);
        var isGameExist = existingGame.IsSome;
        if (isGameExist)
        {
            Log.Information("Active maze game already exists in chat {0}", chatId);
            message = mazeConfig.RepeatAnnouncementMessage;
        }
        else
        {
            var isCreate = await mazeGameService.CreateGame(chatId, mazeConfig);
            isCreate.Match(
                game => Log.Information("Maze game created successfully"),
                () => Log.Error("Failed to create maze game in database"));
        }

        return await Array(messageAssistance.SendMessage(chatId, message, Command, replyMarkup),
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private async Task<Unit> RemoveGame(long chatId)
    {
        var isRemove = await mazeGameService.RemoveGameAndPlayers(chatId);
        if (isRemove)
        {
            Log.Information("Maze game removed successfully from chat {ChatId}", chatId);
        }
        else
        {
            Log.Warning("No maze game found to remove in chat {ChatId}", chatId);
        }

        return unit;
    }
}