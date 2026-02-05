using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;

[Singleton]
public class GlobalAdminCallbackHandler(
    AppConfig appConfig,
    MessageAssistance messageAssistance) : IPrivateCallbackHandler
{
    public const string PrefixKey = "GlobalAdmin";
    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, chatId, telegramId, _, queryId, _) = queryParameters;
        if (!appConfig.GlobalAdminConfig.AdminIds.Contains(telegramId))
        {
            return unit;
        }

        if (!Enum.TryParse(suffix, true, out GlobalAdminSuffix adminSuffix)) return unit;

        var taskResult = adminSuffix switch
        {
            GlobalAdminSuffix.AddDisabledChatConfig => SendRequestAddConfig(telegramId, false),
            GlobalAdminSuffix.AddEnabledChatConfig => SendRequestAddConfig(telegramId, true),
            GlobalAdminSuffix.RemoveChatConfig => SendRequestRemoveConfig(telegramId),
            _ => throw new ArgumentOutOfRangeException(nameof(suffix), suffix, null)
        };

        return await Array(taskResult, messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix)).WhenAll();
    }

    private async Task<Unit> SendRequestAddConfig(long chatId, bool enabled)
    {
        var message =
            $"Отправьте ID чата для добавления {(enabled ? "включенного" : "выключенного")} конфига.\n\nФормат: просто число без -100 (например: 1234567890)\n{GetConfigTag(ChatConfigHandler.AddSuffix, enabled)}";
        return await messageAssistance.SendCommandResponse(chatId, message, nameof(GlobalAdminCallbackHandler),
            replyMarkup: new ForceReplyMarkup());
    }

    private async Task<Unit> SendRequestRemoveConfig(long chatId)
    {
        var message =
            $"Отправьте ID чата для удаления конфига.\n\nФормат: просто число без -100 (например: 1234567890)\n{GetConfigTag(ChatConfigHandler.DeleteSuffix, false)}";
        return await messageAssistance.SendCommandResponse(chatId, message, nameof(GlobalAdminCallbackHandler),
            replyMarkup: new ForceReplyMarkup());
    }

    private static string GetConfigTag(string suffix, bool enabled) => $"\n{ChatConfigHandler.Tag}{suffix}:{enabled}";
}

public enum GlobalAdminSuffix
{
    AddDisabledChatConfig,
    AddEnabledChatConfig,
    RemoveChatConfig
}