using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Service.Buttons;

[Singleton]
public class CaptchaButtons(Random random)
{
    private const int Attempts = 5;

    public InlineKeyboardMarkup GetMarkup(long telegramId, CaptchaConfig captchaConfig)
    {
        var correctAnswer = captchaConfig.CaptchaAnswer;
        var keyboardButtons = new List<InlineKeyboardButton>
        {
            GetCaptchaButton(correctAnswer, telegramId, true),
            GetCaptchaButton(GetWrongAnswer(correctAnswer), telegramId, false),
            GetCaptchaButton(GetWrongAnswer(correctAnswer), telegramId, false),
        };

        var captchaButtons = keyboardButtons
            .OrderBy(_ => random.Next())
            .ToArray();

        return new InlineKeyboardMarkup([captchaButtons]);
    }

    private string GetWrongAnswer(string correctAnswer)
    {
        var chars = correctAnswer.ToCharArray();
        var attempts = 0;
        string newWord;
        do
        {
            attempts++;
            newWord = new string(chars.OrderBy(_ => random.Next()).ToArray());
        } while (newWord == correctAnswer && attempts < Attempts);

        return newWord == correctAnswer ? newWord + Attempts : newWord;
    }

    private static InlineKeyboardButton GetCaptchaButton(string name, long telegramId, bool isCorrect)
    {
        var callback = CallbackService.GetCallback(
            CaptchaCallbackHandler.PrefixKey, name, (CallbackService.UserIdParameter, telegramId));
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}