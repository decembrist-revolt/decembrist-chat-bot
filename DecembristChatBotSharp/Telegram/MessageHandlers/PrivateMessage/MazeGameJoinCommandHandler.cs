using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Service.Buttons;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.PrivateMessage;

[Singleton]
public class MazeGameJoinCommandHandler(
    BotClient botClient,
    MazeGameService mazeGameService,
    MazeGameRepository mazeGameRepository,
    MazeGameButtons mazeGameButtons,
    MazeGameViewService mazeGameViewService,
    MessageAssistance messageAssistance,
    PremiumMemberService premiumMemberService,
    CancellationTokenSource cancelToken,
    ChatConfigService chatConfigService
)
{
    public const string PrefixKey = "MazeJoin";
    public string Prefix => PrefixKey;

    public async Task<Unit> Do(long chatId, long telegramId)
    {
        var maybeConfig = await chatConfigService.GetConfig(chatId, config => config.MazeConfig);
        if (!maybeConfig.TryGetSome(out var mazeConfig))
        {
            return chatConfigService.LogNonExistConfig(unit, nameof(MazeConfig));
        }

        var isPlayerExist =
            await mazeGameRepository.GetPlayer(new MazeGamePlayer.CompositeId(chatId, telegramId));

        return await isPlayerExist.Match(p => SendGameControls(p.Id.TelegramId, chatId, p, mazeConfig),
            () => TryJoinPlayer(chatId, telegramId, mazeConfig));
    }

    private async Task<Unit> TryJoinPlayer(long chatId, long telegramId, MazeConfig mazeConfig)
    {
        var joinResult = await mazeGameService.JoinGame(chatId, telegramId);
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
                        new MazeGamePlayer.CompositeId(chatId, telegramId),
                        bonusInventory
                    );

                    player = updatedPlayer;
                    Log.Information("Premium player {0} received bonus items in maze game", telegramId);
                }

                return await SendGameControls(telegramId, chatId, player, mazeConfig);
            },
            async () =>
            {
                Log.Warning("Failed to join maze game for player {0} in chat {1}", telegramId, chatId);
                return await SendGameNotFound(telegramId, mazeConfig);
            });
    }

    private Task<Unit> SendGameNotFound(long telegramId, MazeConfig mazeConfig) =>
        messageAssistance.SendMessage(telegramId,
            mazeConfig.GameNotFoundMessage, nameof(MazeGameJoinCommandHandler));

    private async Task<Unit> SendGameControls(long telegramId, long chatId, MazeGamePlayer player,
        MazeConfig mazeConfig)
    {
        await SendWelcomeMessage(player, mazeConfig);
        await SendPlayerView(telegramId, chatId, player);
        return unit;
    }

    private async Task<Unit> SendPlayerView(long telegramId, long chatId, MazeGamePlayer player)
    {
        var viewImage = await mazeGameService.RenderPlayerView(chatId, telegramId);
        if (viewImage != null)
        {
            using var stream = new MemoryStream(viewImage, false);

            var inventoryText = mazeGameViewService.FormatInventoryText(player.Inventory);
            var keyboard = mazeGameButtons.GetMazeKeyboard(chatId);

            await botClient.SendPhotoAndLog(
                telegramId,
                stream,
                inventoryText,
                async msg =>
                {
                    await mazeGameRepository.UpdatePlayerLastPhotoMessageId(
                        new MazeGamePlayer.CompositeId(chatId, telegramId),
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

    private Task<Unit> SendWelcomeMessage(MazeGamePlayer player, Entity.Configs.MazeConfig mazeConfig)
    {
        var telegramId = player.Id.TelegramId;
        var welcomeMessage = string.Format(mazeConfig.WelcomeMessage, player.Color, player.ViewRadius);
        return messageAssistance.SendMessage(telegramId, welcomeMessage, nameof(MazeGameJoinCommandHandler));
    }
}