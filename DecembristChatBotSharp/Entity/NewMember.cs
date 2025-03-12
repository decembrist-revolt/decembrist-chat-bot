namespace DecembristChatBotSharp.Entity;

public class NewMember()
{
    public long TelegramId { get; set; }
    public string Username { get; set; }
    public long ChatId { get; set; }
    public int WelcomeMessageId { get; set; }
    public DateTime EnterDate { get; set; }
    
    public NewMember(long telegramId, string username, long chatId, int welcomeMessageId, DateTime enterDate) : this()
    {
        TelegramId = telegramId;
        Username = username;
        ChatId = chatId;
        WelcomeMessageId = welcomeMessageId;
        EnterDate = enterDate;
    }

    public void Deconstruct(out long telegramId, out string username, out long chatId, out int welcomeMessageId, out DateTime enterDate)
    {
        telegramId = TelegramId;
        username = Username;
        chatId = ChatId;
        welcomeMessageId = WelcomeMessageId;
        enterDate = EnterDate;
    }
}