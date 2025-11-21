using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Service;

public record DeepSeekRequest(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("parent_message_id")] string ParentMessageId
);

public record DeepSeekResponse(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("finish_reason")] string FinishReason,
    [property: JsonPropertyName("response_id")] string ResponseId
);

[Singleton]
public class DeepSeekService(
    IHttpClientFactory httpClientFactory,
    AppConfig appConfig)
{
    public async Task<Option<string>> GetChatResponse(string userMessage, long chatId, long userId)
    {
        try
        {
            using var httpClient = httpClientFactory.CreateClient("DeepSeekClient");

            var requestData = new DeepSeekRequest(
                Message: userMessage,
                ParentMessageId: ""
            );

            var jsonRequest = JsonSerializer.Serialize(requestData);

            var request = new HttpRequestMessage(HttpMethod.Post, appConfig.DeepSeekConfig.ApiUrl);
            request.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error("DeepSeek API error: {StatusCode}, {Content}", response.StatusCode, errorContent);
                return None;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            
            var apiResponse = JsonSerializer.Deserialize<DeepSeekResponse>(responseContent);
            
            if (apiResponse == null || string.IsNullOrWhiteSpace(apiResponse.Message))
            {
                Log.Warning("DeepSeek API returned empty or invalid response");
                return None;
            }
            
            Log.Information("DeepSeek response for chat {ChatId}, user {UserId}: {Message} (ResponseId: {ResponseId})", 
                chatId, userId, apiResponse.Message, apiResponse.ResponseId);

            return Some(apiResponse.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get DeepSeek response for chat {ChatId}, user {UserId}", chatId, userId);
            return None;
        }
    }
}

