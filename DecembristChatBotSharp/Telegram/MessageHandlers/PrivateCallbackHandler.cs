using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class PrivateCallbackHandler(
    BotClient botClient,
    AppConfig appConfig,
    InventoryService inventoryService,
    ProfileService profileService,
    CancellationTokenSource cancelToken)
{
    public async Task<Unit> Do(CallbackQuery callbackQuery)
    {
        await botClient.AnswerCallbackQuery(callbackQuery.Id);

        var data = callbackQuery.Data;
        var messageId = callbackQuery.Message!.Id;
        var telegramId = callbackQuery.From.Id;

        var trySend = data switch
        {
            not null when data.Split("=") is [_, var chatIdText] &&
                          long.TryParse(chatIdText, out var targetChatId) => targetChatId switch
            {
                _ when data.Contains(ProfileService.BackCallback) =>
                    SendWelcomeProfile(messageId, telegramId, targetChatId),
                _ when data.Contains(ProfileService.LorCallback) => SendLorView(messageId, telegramId, targetChatId),
                _ when data.Contains(ProfileService.CreateLorCallback) => SendRequestLorKey(targetChatId, telegramId),
                _ when data.Contains(ProfileService.EditLorCallback) => SendRequestEdit(targetChatId, telegramId),
                _ when data.Contains(PrivateMessageHandler.InventoryCommandSuffix)
                    => SendInventoryView(messageId, telegramId, targetChatId),
                _ => throw new ArgumentOutOfRangeException()
            },
            _ => botClient.SendMessage(telegramId, "OK", cancellationToken: cancelToken.Token)
        };
        return await TryAsync(trySend).Match(
            message => Log.Information("Edit private {0} to {1}", message.Text?.Replace('\n', ' '), telegramId),
            ex => Log.Error(ex, "Failed to edit private message to {0}", telegramId));
    }

    private async Task<Message> SendWelcomeProfile(int messageId, long telegramId, long chatId)
    {
        var markup = await profileService.GetProfileMarkup(telegramId, chatId);
        var message = appConfig.MenuConfig.WelcomeMessage;
        return await profileService.EditProfileMessage(telegramId, chatId, messageId, markup, message);
    }

    private async Task<Message> SendInventoryView(int messageId, long telegramId, long chatId)
    {
        var markup = new[] { InlineKeyboardButton.WithCallbackData("Назад", ProfileService.BackCallback + chatId) };
        var message = await inventoryService.GetInventory(chatId, telegramId);
        return await profileService.EditProfileMessage(telegramId, chatId, messageId, markup, message);
    }

    private async Task<Message> SendLorView(int messageId, long telegramId, long chatId)
    {
        var markup = new InlineKeyboardMarkup
        {
            InlineKeyboard =
            [
                [InlineKeyboardButton.WithCallbackData("CreateLor", ProfileService.CreateLorCallback + chatId)],
                [InlineKeyboardButton.WithCallbackData("EditLor", ProfileService.EditLorCallback + chatId)],
                [InlineKeyboardButton.WithCallbackData("Назад", ProfileService.BackCallback + chatId)]
            ]
        };
        var message = appConfig.MenuConfig.LorDescription;
        return await profileService.EditProfileMessage(telegramId, chatId, messageId, markup, message);
    }

    private Task<Message> SendRequestEdit(long targetChatId, long chatId)
    {
        var markup = new ForceReplyMarkup { InputFieldPlaceholder = appConfig.LorConfig.LorTip };
        var message = "Введите ключ для изменения содержания\n" +
                      LorService.GetLorTag(LorReplyHandler.LorEditSuffix, targetChatId);
        return botClient.SendMessage(chatId, message, replyMarkup: markup);
    }

    private Task<Message> SendRequestLorKey(long targetChatId, long chatId)
    {
        var markup = new ForceReplyMarkup { InputFieldPlaceholder = appConfig.LorConfig.LorTip };
        var message = string.Format(appConfig.LorConfig.LorKeyRequest,
            LorService.GetLorTag(LorReplyHandler.LorCreateSuffix, targetChatId));
        return botClient.SendMessage(chatId, message, replyMarkup: markup);
    }
}