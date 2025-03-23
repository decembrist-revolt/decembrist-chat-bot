using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Reddit;
using Lamar;
using Serilog;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class RedditMemeCommandHandler(
    AppConfig appConfig,
    MemberItemRepository memberItemRepository,
    RedditService redditService,
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    BotClient botClient,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public const string CommandKey = "/redditmeme";
    
    public string Command => CommandKey;
    public string Description => "Generate random reddit meme";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var telegramId = parameters.TelegramId;
        var chatId = parameters.ChatId;
        var messageId = parameters.MessageId;

        if (!await adminUserRepository.IsAdmin(telegramId))
        {
            if (!await memberItemRepository.RemoveMemberItem(telegramId, chatId, MemberItemType.RedditMeme))
            {
                return await messageAssistance.SendNoItems(chatId);
            }
        }

        var maybeMeme = await redditService.GetRandomMeme();
        if (maybeMeme.IsNone) maybeMeme = await redditService.GetRandomMeme();
        if (maybeMeme.IsNone)
        {
            await Array(SendRedditErrorMessage(chatId),
                memberItemRepository.AddMemberItem(telegramId, chatId, MemberItemType.RedditMeme).UnitTask()).WhenAll();
        }

        return await maybeMeme.IfSomeAsync(meme => SendMemeAndDeleteSource(chatId, messageId, meme));
    }

    private Task<Unit> SendMemeAndDeleteSource(long chatId, int messageId, RedditRandomMeme meme)
    {
        var message = $"{meme.Url.EscapeMarkdown()}\n[Источник]({meme.Permalink.EscapeMarkdown()})";
        return Array(botClient.SendMessageAndLog(chatId, message, ParseMode.MarkdownV2,
                _ => Log.Information("Sent random meme to chat {0}", chatId),
                ex => Log.Error(ex, "Failed to send random meme {0} to chat {1}", message, chatId),
                cancelToken.Token),
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private async Task<Unit> SendRedditErrorMessage(long chatId)
    {
        var message = appConfig.RedditConfig.RedditErrorMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent reddit error message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send reddit error message to chat {0}", chatId),
            cancelToken.Token);
    }
}