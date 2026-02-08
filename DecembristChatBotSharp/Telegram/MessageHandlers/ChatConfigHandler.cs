using DecembristChatBotSharp.Mongo;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class ChatConfigHandler(
    MessageAssistance messageAssistance,
    ChatConfigRepository chatConfigRepository,
    AppConfig appConfig,
    CancellationTokenSource cancelToken,
    BotClient botClient)
{
    public const string Tag = "#ChatConfig";
    public const string AddSuffix = "Add";
    public const string EnableSuffix = "Enable";
    public const string DisableSuffix = "Disable";
    public const string DeleteSuffix = "Delete";

    public async Task<Unit> Do(Message message)
    {
        var telegramId = message.From!.Id;

        if (!appConfig.GlobalAdminConfig.AdminIds.Contains(telegramId))
        {
            return await SendNotAdmin(telegramId);
        }

        var replyText = message.ReplyToMessage!.Text;
        var messageText = message.Text!;

        return await ParseReplyText(replyText).Match(
            None: async () => await SendInvalidIdMessage(telegramId),
            Some: async suffix =>
            {
                await messageAssistance.DeleteCommandMessage(telegramId, message.ReplyToMessage.Id, Tag);
                await messageAssistance.DeleteCommandMessage(telegramId, message.Id, Tag);

                if (!long.TryParse(messageText.Trim(), out var targetChatId))
                {
                    return await SendInvalidIdMessage(telegramId);
                }

                return suffix switch
                {
                    AddSuffix => await HandleAddConfig(targetChatId, telegramId),
                    EnableSuffix => await ToggleConfig(targetChatId, telegramId, true),
                    DisableSuffix => await ToggleConfig(targetChatId, telegramId, false),
                    DeleteSuffix => await HandleDeleteConfig(targetChatId, telegramId),
                    _ => throw new InvalidOperationException($"Unknown suffix: {suffix}"),
                };
            });
    }

    private async Task<Unit> ToggleConfig(long chatId, long telegramId, bool enabled)
    {
        var success = await chatConfigRepository.ToggleChatConfig(chatId, enabled);
        return success
            ? await SendToggleSuccessMessage(chatId, telegramId, enabled)
            : await messageAssistance.SendMessage(telegramId,
                string.Format(appConfig.ChatConfigMessages.ToggleFailedMessage, chatId), Tag);
    }

    private async Task<Unit> HandleAddConfig(long chatId, long telegramId)
    {
        var existingConfig = await chatConfigRepository.IsChatConfigExist(chatId);
        if (!existingConfig.TryGetSome(out var isExist))
        {
            return await SendFailedAddMessage(telegramId, chatId);
        }

        if (isExist)
        {
            return await SendAlreadyExistsMessage(telegramId, chatId);
        }

        var newConfig = appConfig.ChatConfigTemplate with { ChatId = chatId };
        var result = await chatConfigRepository.InsertChatConfig(newConfig);

        return result
            ? await SendSuccessAddMessage(telegramId, chatId)
            : await SendFailedAddMessage(telegramId, chatId);
    }

    private async Task<Unit> HandleDeleteConfig(long chatId, long telegramId)
    {
        var result = await chatConfigRepository.DeleteChatConfig(chatId);
        return result
            ? await SendSuccessDeleteMessage(telegramId, chatId)
            : await SendNotFoundMessage(telegramId, chatId);
    }

    private Option<string> ParseReplyText(string replyText) =>
        replyText.Split(Tag) is [_, var suffix]
            ? suffix
            : None;

    private Task<Unit> SendNotAdmin(long telegramId) =>
        messageAssistance.SendMessage(telegramId, appConfig.ChatConfigMessages.AdminOnlyMessage, Tag);

    private Task<Unit> SendInvalidIdMessage(long telegramId) =>
        messageAssistance.SendMessage(telegramId, appConfig.ChatConfigMessages.InvalidIdMessage, Tag);

    private Task<Unit> SendToggleSuccessMessage(long chatId, long telegramId, bool enabled) =>
        messageAssistance.SendMessage(telegramId,
            string.Format(appConfig.ChatConfigMessages.ToggleSuccessMessage, chatId, enabled ? "включен" : "выключен"),
            Tag);

    private Task<Unit> SendAlreadyExistsMessage(long telegramId, long chatId) =>
        messageAssistance.SendMessage(telegramId,
            string.Format(appConfig.ChatConfigMessages.AlreadyExistsMessage, chatId), Tag);

    private Task<Message> SendTemplateErrorMessage(long telegramId) =>
        botClient.SendMessage(telegramId, appConfig.ChatConfigMessages.TemplateErrorMessage,
            cancellationToken: cancelToken.Token);

    private Task<Unit> SendSuccessAddMessage(long telegramId, long chatId) =>
        messageAssistance.SendMessage(telegramId, string.Format(appConfig.ChatConfigMessages.SuccessAddMessage, chatId),
            Tag);

    private Task<Unit> SendFailedAddMessage(long telegramId, long chatId) =>
        messageAssistance.SendMessage(telegramId, string.Format(appConfig.ChatConfigMessages.FailedAddMessage, chatId),
            Tag);

    private Task<Unit> SendSuccessDeleteMessage(long telegramId, long chatId) =>
        messageAssistance.SendMessage(telegramId,
            string.Format(appConfig.ChatConfigMessages.SuccessDeleteMessage, chatId), Tag);

    private Task<Unit> SendNotFoundMessage(long telegramId, long chatId) =>
        messageAssistance.SendMessage(telegramId, string.Format(appConfig.ChatConfigMessages.NotFoundMessage, chatId),
            Tag);
}