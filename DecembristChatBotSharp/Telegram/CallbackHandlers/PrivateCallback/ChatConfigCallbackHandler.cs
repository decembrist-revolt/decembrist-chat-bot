using System.Text;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service.Buttons;
using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Lamar;
using Serilog;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;

[Singleton]
public class ChatConfigCallbackHandler(
    AppConfig appConfig,
    ChatConfigRepository chatConfigRepository,
    MessageAssistance messageAssistance,
    ChatConfigButton chatConfigButton) : IPrivateCallbackHandler
{
    public const string PrefixKey = "ChatConfig";
    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, chatId, telegramId, messageId, queryId, _) = queryParameters;
        if (!appConfig.GlobalAdminConfig.AdminIds.Contains(telegramId))
        {
            Log.Error("{telegramId} is not a global admin, cannot use chat config callback", telegramId);
            return unit;
        }

        Log.Information("Global admin {telegramId} triggered chat config callback with {suffix} in chat {chatId}",
            telegramId, suffix, chatId);

        if (!Enum.TryParse(suffix, true, out GlobalAdminSuffix adminSuffix)) return unit;

        var taskResult = adminSuffix switch
        {
            GlobalAdminSuffix.AddDisabledChatConfig => SendRequestAddConfig(telegramId),
            GlobalAdminSuffix.EnableChatConfig => SendRequestEnabledConfig(telegramId),
            GlobalAdminSuffix.DisableChatConfig => SendRequestDisabledConfig(telegramId),
            GlobalAdminSuffix.RemoveChatConfig => SendRequestRemoveConfig(telegramId),
            GlobalAdminSuffix.ChatConfigList => SendChatList(telegramId, messageId),
            _ => throw new ArgumentOutOfRangeException(nameof(suffix), suffix, null)
        };

        return await Array(taskResult, messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix)).WhenAll();
    }

    private async Task<Unit> SendRequestAddConfig(long chatId)
    {
        var message = string.Format(appConfig.ChatConfigMessages.AddConfigRequest,
            GetConfigTag(ChatConfigHandler.AddSuffix));
        return await messageAssistance.SendCommandResponse(chatId, message, nameof(ChatConfigCallbackHandler),
            replyMarkup: new ForceReplyMarkup());
    }

    private async Task<Unit> SendRequestEnabledConfig(long chatId)
    {
        var message = string.Format(appConfig.ChatConfigMessages.EnableConfigRequest,
            GetConfigTag(ChatConfigHandler.EnableSuffix));
        return await messageAssistance.SendCommandResponse(chatId, message, nameof(ChatConfigCallbackHandler),
            replyMarkup: new ForceReplyMarkup());
    }

    private async Task<Unit> SendRequestDisabledConfig(long chatId)
    {
        var message = string.Format(appConfig.ChatConfigMessages.DisableConfigRequest,
            GetConfigTag(ChatConfigHandler.DisableSuffix));
        return await messageAssistance.SendCommandResponse(chatId, message, nameof(ChatConfigCallbackHandler),
            replyMarkup: new ForceReplyMarkup());
    }

    private async Task<Unit> SendRequestRemoveConfig(long chatId)
    {
        var message = string.Format(appConfig.ChatConfigMessages.DeleteConfigRequest,
            GetConfigTag(ChatConfigHandler.DeleteSuffix));
        return await messageAssistance.SendCommandResponse(chatId, message, nameof(ChatConfigCallbackHandler),
            replyMarkup: new ForceReplyMarkup());
    }

    private async Task<Unit> SendChatList(long chatId, int messageId)
    {
        var chatIds = await chatConfigRepository.GetChatIds();
        var sb = new StringBuilder()
            .AppendLine(string.Format(appConfig.ChatConfigMessages.ChatListMessage, chatIds.Count).EscapeMarkdown())
            .AppendLine();
        if (chatIds.Count > 0)
        {
            foreach (var id in chatIds)
            {
                sb.AppendLine($"•  `{id}`");
            }
        }

        var message = sb.ToString();
        var buttons = chatConfigButton.GetMarkup();
        return await messageAssistance.EditMessageAndLog(chatId, messageId, message, nameof(ChatConfigCallbackHandler),
            buttons, ParseMode.Markdown);
    }

    private static string GetConfigTag(string suffix) => $"\n{ChatConfigHandler.Tag}{suffix}";
}

public enum GlobalAdminSuffix
{
    AddDisabledChatConfig,
    EnableChatConfig,
    DisableChatConfig,
    RemoveChatConfig,
    ChatConfigList
}