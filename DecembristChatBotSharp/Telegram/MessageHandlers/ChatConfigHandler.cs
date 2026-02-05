using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class ChatConfigHandler(
    MessageAssistance messageAssistance,
    ChatConfigRepository chatConfigRepository,
    ChatConfigService chatConfigService,
    AppConfig appConfig,
    CancellationTokenSource cancelToken,
    BotClient botClient)
{
    public const string Tag = "#ChatConfig";
    public const string AddSuffix = "Add";
    public const string DeleteSuffix = "Delete";
    private const string AdminOnlyMessage = "Только глобальные администраторы могут использовать эту команду.";

    public async Task<Message> Do(Message message)
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
            Some: async tuple =>
            {
                await messageAssistance.DeleteCommandMessage(telegramId, message.ReplyToMessage.Id, Tag);
                await messageAssistance.DeleteCommandMessage(telegramId, message.Id, Tag);
                var (suffix, enabled) = tuple;
                return suffix switch
                {
                    _ when suffix == AddSuffix => await HandleAddConfig(messageText, telegramId, enabled),
                    _ when suffix == DeleteSuffix => await HandleDeleteConfig(messageText, telegramId),
                    _ => throw new InvalidOperationException($"Unknown suffix: {suffix}"),
                };
            });
    }

    private async Task<Message> HandleAddConfig(string messageText, long telegramId, bool enabled)
    {
        if (!long.TryParse(messageText.Trim(), out var rawChatId))
        {
            return await SendInvalidIdMessage(telegramId);
        }

        var chatId = NormalizeChatId(rawChatId);

        var existingConfig = await chatConfigRepository.IsChatConfigExist(chatId);
        if (existingConfig.TryGetSome(out var isExist) && isExist)
        {
            return await SendAlreadyExistsMessage(telegramId, chatId);
        }

        if (existingConfig.IsNone)
        {
            return await SendTemplateErrorMessage(telegramId);
        }

        if (appConfig.ChatConfigTemplate == null)
        {
            Log.Error("ChatConfigTemplate is null in AppConfig");
            return await SendTemplateErrorMessage(telegramId);
        }

        var newConfig = chatConfigService.GetNewConfig(chatId, enabled);
        var result = await chatConfigService.InsertChatConfig(newConfig);

        return result
            ? await SendSuccessAddMessage(telegramId, chatId, enabled)
            : await SendFailedAddMessage(telegramId, chatId);
    }

    private static long NormalizeChatId(long rawChatId) =>
        rawChatId.ToString().StartsWith("-100") ? rawChatId : long.Parse($"-100{rawChatId}");

    private async Task<Message> HandleDeleteConfig(string messageText, long telegramId)
    {
        if (!long.TryParse(messageText.Trim(), out var rawChatId))
        {
            return await SendInvalidIdMessage(telegramId);
        }

        var chatId = NormalizeChatId(rawChatId);
        var result = await chatConfigService.DeleteChatConfig(chatId);

        return result
            ? await SendSuccessDeleteMessage(telegramId, chatId)
            : await SendNotFoundMessage(telegramId, chatId);
    }

    private static Option<(string suffix, bool enabled)> ParseReplyText(string replyText) =>
        replyText.Split(Tag) is [_, var suffixAndEnabled] &&
        suffixAndEnabled.Split(":") is [var suffix, var enabledStr]
        && bool.TryParse(enabledStr, out var enabled)
            ? (suffix, enabled)
            : None;

    private Task<Message> SendNotAdmin(long telegramId) =>
        botClient.SendMessage(telegramId, AdminOnlyMessage, cancellationToken: cancelToken.Token);

    private Task<Message> SendInvalidIdMessage(long telegramId) =>
        botClient.SendMessage(telegramId, "Неверный формат ID чата. Отправьте число (например: 1234567890)",
            cancellationToken: cancelToken.Token);

    private Task<Message> SendAlreadyExistsMessage(long telegramId, long chatId) =>
        botClient.SendMessage(telegramId, $"Конфиг для чата {chatId} уже существует",
            cancellationToken: cancelToken.Token);

    private Task<Message> SendTemplateErrorMessage(long telegramId) =>
        botClient.SendMessage(telegramId, "Ошибка при чтении шаблона конфига",
            cancellationToken: cancelToken.Token);

    private Task<Message> SendSuccessAddMessage(long telegramId, long chatId, bool enabled) =>
        botClient.SendMessage(telegramId,
            $"Конфиг для чата {chatId} успешно добавлен, всё ({(enabled ? "включено" : "выключено")})",
            cancellationToken: cancelToken.Token);

    private Task<Message> SendFailedAddMessage(long telegramId, long chatId) =>
        botClient.SendMessage(telegramId, $"Не удалось добавить конфиг для чата {chatId}",
            cancellationToken: cancelToken.Token);

    private Task<Message> SendSuccessDeleteMessage(long telegramId, long chatId) =>
        botClient.SendMessage(telegramId, $"Конфиг для чата {chatId} успешно удален",
            cancellationToken: cancelToken.Token);

    private Task<Message> SendNotFoundMessage(long telegramId, long chatId) =>
        botClient.SendMessage(telegramId, $"Конфиг для чата {chatId} не найден",
            cancellationToken: cancelToken.Token);
}