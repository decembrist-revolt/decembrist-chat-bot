using LiteDB;

namespace DecembristChatBotSharp.Entity;

public class NewMember()
{
    public ObjectId? Id { get; set; }
    public long TelegramId { get; set; }
    public string Username { get; set; }
    public long ChatId { get; set; }
    public int WelcomeMessageId { get; set; }
    public DateTime EnterDate { get; set; }
    public int CaptchaRetryCount { get; set; } = 0;
    
    public NewMember(
        long telegramId, 
        string username, 
        long chatId, 
        int welcomeMessageId, 
        DateTime enterDate,
        int captchaRetryCount = 0) : this()
    {
        TelegramId = telegramId;
        Username = username;
        ChatId = chatId;
        WelcomeMessageId = welcomeMessageId;
        EnterDate = enterDate;
        CaptchaRetryCount = captchaRetryCount;
    }

    public void Deconstruct(
        out long telegramId,
        out string username,
        out long chatId,
        out int welcomeMessageId,
        out DateTime enterDate,
        out int captchaRetryCount)
    {
        telegramId = TelegramId;
        username = Username;
        chatId = ChatId;
        welcomeMessageId = WelcomeMessageId;
        enterDate = EnterDate;
        captchaRetryCount = CaptchaRetryCount;
    }
}