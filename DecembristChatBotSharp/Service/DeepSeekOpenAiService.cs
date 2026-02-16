using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lamar;
using OpenAI;
using OpenAI.Chat;
using Serilog;

namespace DecembristChatBotSharp.Service;

public record ModerationResponse(
    [property: JsonPropertyName("isScam")] bool IsScam
);

[Singleton]
public class DeepSeekOpenAiService(AppConfig appConfig)
{
    private readonly ChatClient _client = new OpenAIClient(
            new ApiKeyCredential(appConfig.DeepSeekConfig!.BearerToken),
            new OpenAIClientOptions { Endpoint = new Uri(appConfig.DeepSeekConfig.ApiUrl) })
        .GetChatClient("deepseek-chat");

    public async Task<Option<bool>> GetModerateVerdict(string messageText, long chatId, long userId)
    {
        var prompt = $$"""
                       Ты - модератор чата. Твоя задача определить, является ли сообщение спамом, мошенничеством (скамом) или рекламой.

                       Признаки скама/спама:
                       - Предложения "лёгкого заработка", "работы на дому" с высокой оплатой
                       - Упоминание конкретных сумм денег за простую работу
                       - Призывы написать в личные сообщения для получения "выгодного предложения"
                       - "Ежедневная оплата", "удалённая работа", "доход от X рублей/долларов"
                       - Фразы типа "ищу N человек", "пиши +", "пиши сюда"
                       - Обещания заработка с телефона или компьютера без специальных навыков
                       - Реклама сторонних сервисов, каналов, ботов (кроме тематических обсуждений)

                       Сообщение для проверки:
                       "{{messageText}}"

                       Ответь СТРОГО в формате JSON, без дополнительного текста:
                       {"isScam": true}
                       или
                       {"isScam": false}

                       Отвечай только JSON, ничего больше.
                       """;

        try
        {
            Log.Debug("Sending moderation request to DeepSeek for chat {ChatId}, user {UserId}", chatId, userId);

            var completion = await _client.CompleteChatAsync(prompt);

            if (completion?.Value?.Content == null || completion.Value.Content.Count == 0)
            {
                Log.Warning("DeepSeek moderation returned empty response for chat {ChatId}, user {UserId}", chatId,
                    userId);
                return None;
            }

            var response = completion.Value.Content[0].Text?.Trim();

            if (string.IsNullOrWhiteSpace(response))
            {
                Log.Warning("DeepSeek moderation returned empty message for chat {ChatId}, user {UserId}", chatId,
                    userId);
                return None;
            }

            try
            {
                var jsonResponse = JsonSerializer.Deserialize<ModerationResponse>(response);

                if (jsonResponse == null)
                {
                    Log.Warning("Failed to deserialize moderation response for chat {ChatId}: {Response}", chatId,
                        response);
                    return None;
                }

                Log.Information("DeepSeek moderation verdict for chat {ChatId}, user {UserId}: isScam={IsScam}",
                    chatId, userId, jsonResponse.IsScam);

                return jsonResponse.IsScam;
            }
            catch (JsonException ex)
            {
                Log.Error(ex, "Failed to parse moderation JSON response for chat {ChatId}: {Response}", chatId,
                    response);
                return None;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get DeepSeek moderation verdict for chat {ChatId}, user {UserId}", chatId, userId);
            return None;
        }
    }

    public async Task<Option<string>> GetChatResponse(string userMessage, long chatId, long userId)
    {
        try
        {
            Log.Debug("Sending request to DeepSeek for chat {ChatId}, user {UserId}", chatId, userId);

            var completion = await _client.CompleteChatAsync(userMessage);

            if (completion?.Value?.Content == null || completion.Value.Content.Count == 0)
            {
                Log.Warning("DeepSeek API returned empty response for chat {ChatId}, user {UserId}", chatId, userId);
                return None;
            }

            var message = completion.Value.Content[0].Text;

            if (string.IsNullOrWhiteSpace(message))
            {
                Log.Warning("DeepSeek API returned empty message for chat {ChatId}, user {UserId}", chatId, userId);
                return None;
            }

            Log.Information("DeepSeek response for chat {ChatId}, user {UserId}: {MessageLength} characters",
                chatId, userId, message.Length);

            return Some(message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get DeepSeek response for chat {ChatId}, user {UserId}", chatId, userId);
            return None;
        }
    }
}