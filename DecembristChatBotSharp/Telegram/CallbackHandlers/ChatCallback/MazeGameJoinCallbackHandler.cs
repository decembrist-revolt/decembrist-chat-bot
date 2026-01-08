using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;

[Singleton]
public class MazeGameJoinCallbackHandler(
    BotClient botClient,
    MazeGameService mazeGameService,
    MazeGameRepository mazeGameRepository,
    MazeGameButtons mazeGameButtons,
    MessageAssistance messageAssistance,
    AppConfig appConfig,
    PremiumMemberService premiumMemberService,
    CancellationTokenSource cancelToken) : IChatCallbackHandler
{
    public const string PrefixKey = "MazeJoin";
    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, _, chatId, telegramId, messageId, queryId, _) = queryParameters;

        var joinResult = await mazeGameService.JoinGame(chatId, messageId, telegramId);

        return await joinResult.MatchAsync(
            async player =>
            {
                Log.Information("Player {0} joined maze game in chat {1}", telegramId, chatId);

                // Check if player is premium and grant bonus items
                var isPremium = await premiumMemberService.IsPremium(telegramId, chatId);
                if (isPremium)
                {
                    var bonusInventory = new MazePlayerInventory(
                        player.Inventory.Swords + 1,
                        player.Inventory.Shields + 1,
                        player.Inventory.Shovels + 1,
                        player.Inventory.ViewExpanders + 1
                    );
                    
                    var updatedPlayer = player with { Inventory = bonusInventory };
                    await mazeGameRepository.UpdatePlayerInventory(
                        new MazeGamePlayer.CompositeId(chatId, messageId, telegramId),
                        bonusInventory
                    );
                    
                    player = updatedPlayer;
                    Log.Information("Premium player {0} received bonus items in maze game", telegramId);
                }

                // Answer callback
                await messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix,
                    "Вы вступили в игру! Проверьте личные сообщения для управления.", showAlert: false);

                // Send controls to private chat
                return await SendGameControls(telegramId, chatId, messageId, player);
            },
            async () =>
            {
                Log.Warning("Failed to join maze game for player {0} in chat {1}", telegramId, chatId);
                return await messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix,
                    "Не удалось вступить в игру. Возможно игра уже завершена.", showAlert: true);
            });
    }

    private async Task<Unit> SendGameControls(long telegramId, long chatId, int messageId, MazeGamePlayer player)
    {
        var welcomeMessage = string.Format(appConfig.MazeConfig.WelcomeMessage, player.Color, player.ViewRadius);

        // Отправляем приветственное сообщение
        await botClient.SendMessageAndLog(
            telegramId,
            welcomeMessage,
            _ => Log.Information("Sent maze welcome to player {0}", telegramId),
            ex => Log.Error(ex, "Failed to send maze welcome to player {0}", telegramId),
            cancelToken.Token
        );

        // Отправляем начальное фото с клавиатурой
        var viewImage = await mazeGameService.RenderPlayerView(chatId, messageId, telegramId);
        if (viewImage != null)
        {
            using var stream = new MemoryStream(viewImage, false);
            
            var inventoryText = mazeGameButtons.FormatInventoryText(player.Inventory);
            var keyboard = mazeGameButtons.CreateMazeKeyboard(chatId, messageId);

            await botClient.SendPhotoAndLog(
                telegramId,
                stream,
                inventoryText,
                async msg =>
                {
                    await mazeGameRepository.UpdatePlayerLastPhotoMessageId(
                        new MazeGamePlayer.CompositeId(chatId, messageId, telegramId),
                        msg.MessageId
                    );
                    Log.Information("Sent initial maze view to player {0}", telegramId);
                },
                ex => Log.Error(ex, "Failed to send initial maze view to player {0}", telegramId),
                cancelToken.Token,
                keyboard
            );
        }

        return unit;
    }
}

