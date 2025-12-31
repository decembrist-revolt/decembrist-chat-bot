using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using static DecembristChatBotSharp.Service.CallbackService;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class MazeGameCommandHandler(
    AppConfig appConfig,
    BotClient botClient,
    MessageAssistance messageAssistance,
    AdminUserRepository adminUserRepository,
    MazeGameService mazeGameService,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public string Command => "/mazegame";
    public string Description => "Start a maze game (Admin only)";
    public CommandLevel CommandLevel => CommandLevel.Admin;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        var isAdmin = await adminUserRepository.IsAdmin((telegramId, chatId));
        if (!isAdmin)
        {
            return await Array(
                messageAssistance.SendAdminOnlyMessage(chatId, telegramId),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)
            ).WhenAll();
        }

        // Send announcement message with join button
        var callback = GetCallback<string>("MazeJoin", "");
        var button = InlineKeyboardButton.WithCallbackData("Вступить", callback);
        var keyboard = new InlineKeyboardMarkup(button);

        var message = "🎮 Игра лабиринт началась!\n\n" +
                      "Нажмите кнопку чтобы вступить в игру.\n" +
                      "Цель: найти выход из лабиринта первым и получить 5 коробок!";

        var sentMessage = await botClient.SendMessage(
            chatId,
            message,
            replyMarkup: keyboard,
            cancellationToken: cancelToken.Token
        ).ToTryAsync().Match(Optional, ex =>
        {
            Log.Error(ex, "Failed to send maze game announcement message to chat {ChatId}", chatId);
            return Option<Message>.None;
        });

        if (sentMessage.IsNone) return unit;

        // Create the maze game in database
        sentMessage
            .BindAsync(msg => mazeGameService.CreateGame(chatId, msg.MessageId).ToAsync())
            .Match(
                game => Log.Information("Maze game created successfully"),
                () => Log.Error("Failed to create maze game in database"));

        // Delete the command message
        return await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
    }
}