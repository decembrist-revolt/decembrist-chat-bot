using DecembristChatBotSharp.Entity;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp;

public class CheckCaptchaScheduler(
    BotClient bot, 
    AppConfig appConfig, 
    Database db,
    CancellationToken cancelToken)
{
    private Timer? _timer;

    public Unit Start()
    {
        var interval = TimeSpan.FromSeconds(appConfig.CheckCaptchaIntervalSeconds);

        _timer = new Timer(
            _ => CheckCaptcha().Wait(cancelToken), null, interval, interval);

        cancelToken.Register(_ => _timer.Dispose(), null);

        return unit;
    }

    private async Task<Unit> CheckCaptcha()
    {
        var olderThanUtc = DateTime.UtcNow.AddSeconds(-appConfig.CaptchaTimeSeconds);
        var members = db.GetNewMembers(olderThanUtc);
        await Task.WhenAll(members.Select(HandleExpiredMember));
        return unit;
    }

    private async Task<Unit> HandleExpiredMember(NewMember newMember)
    {
        var (telegramId, username, chatId, welcomeMessageId, _, _) = newMember;

        await Task.WhenAll(
            BanMember(chatId, telegramId, username),
            DeleteWelcomeMessage(chatId, welcomeMessageId, username)
        );

        return unit;
    }

    private async Task<Unit> BanMember(long chatId, long telegramId, string username)
    {
        var result = await TryAsync(bot.BanChatMember(
            chatId: chatId,
            userId: telegramId,
            untilDate: DateTime.UtcNow.AddSeconds(0)
        ).UnitTask);

        return result.Match(
            Succ: _ => OnBannedUser(telegramId, username, chatId),
            Fail: ex => Log.Error(ex, "Failed to ban user {Username} in chat {ChatId}", username, chatId)
        );
    }

    private Unit OnBannedUser(long telegramId, string username, long chatId)
    {
        Log.Information("User {0} banned cause bad captcha in chat {1}", username, chatId);

        if (db.RemoveNewMember(telegramId, chatId))
        {
            Log.Information("User {Username} removed from new members", username);
        }
        else
        {
            Log.Error("Failed to remove user {Username} from new members", username);
        }

        return unit;
    }

    private async Task<Unit> DeleteWelcomeMessage(long chatId, int welcomeMessageId, string username)
    {
        var result = await TryAsync(bot.DeleteMessage(chatId, welcomeMessageId).UnitTask);
        return result.Match(
            Succ: _ => Log.Information("Deleted welcome message for user {0} in chat {1}", username, chatId),
            Fail: ex => Log.Error(ex, "Failed to delete welcome message for user {0} in chat {1}", username, chatId)
        );
    }
}