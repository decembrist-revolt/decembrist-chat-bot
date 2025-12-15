using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Quartz;
using Serilog;

namespace DecembristChatBotSharp.Scheduler;

[Singleton]
public class QuizGeneratorJob(
    AppConfig appConfig,
    QuizService quizService,
    QuizRepository quizRepository) : IRegisterJob
{
    public async Task Register(IScheduler scheduler)
    {
        if (appConfig.QuizConfig is not { Enabled: true })
        {
            Log.Information("Quiz feature is disabled, skipping QuizGeneratorJob registration");
            return;
        }

        var triggerKey = new TriggerKey(nameof(QuizGeneratorJob));

        var job = JobBuilder.Create<QuizGeneratorJob>()
            .WithIdentity(nameof(QuizGeneratorJob))
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .WithCronSchedule(appConfig.QuizConfig.QuestionGenerationCronUtc)
            .Build();

        var existingTrigger = await scheduler.GetTrigger(triggerKey);

        if (existingTrigger != null)
        {
            await scheduler.RescheduleJob(triggerKey, trigger);
            Log.Information("QuizGeneratorJob rescheduled with schedule: {Cron}",
                appConfig.QuizConfig.QuestionGenerationCronUtc);
        }
        else
        {
            await scheduler.ScheduleJob(job, trigger);
            Log.Information("QuizGeneratorJob registered with schedule: {Cron}",
                appConfig.QuizConfig.QuestionGenerationCronUtc);
        }
    }

    public async Task Execute(IJobExecutionContext context)
    {
        Log.Information("QuizGeneratorJob started");

        var allowedChats = appConfig.AllowedChatConfig.AllowedChatIds;
        if (allowedChats is not { Count: not 0 })
        {
            Log.Warning("No allowed chats configured for quiz");
            return;
        }

        foreach (var chatId in allowedChats)
        {
            await GenerateAndSendQuiz(chatId);
        }

        Log.Information("QuizGeneratorJob completed");
    }

    private async Task GenerateAndSendQuiz(long chatId)
    {
        // Check if there's already an active question
        var activeQuestion = await quizRepository.GetActiveQuestion(chatId);

        await activeQuestion.MatchAsync(async question =>
            {
                // Check if question is old enough to auto-close
                var questionAge = DateTime.UtcNow - question.CreatedAtUtc;
                var autoCloseThreshold = TimeSpan.FromMinutes(appConfig.QuizConfig!.AutoCloseUnansweredMinutes);

                if (questionAge < autoCloseThreshold)
                {
                    Log.Information("Chat {ChatId} already has an active quiz question (age: {Age}), skipping",
                        chatId, questionAge);
                    return unit;
                }

                // Question is old, auto-close it
                Log.Information("Auto-closing old unanswered quiz question in chat {ChatId} (age: {Age})",
                    chatId, questionAge);

                // Edit message to show correct answer and that nobody answered
                await quizService.CloseUnansweredQuestion(question);

                // Delete question from database
                await quizRepository.DeleteQuestion(question.Id);

                // Now generate new question
                await GenerateNewQuestion(chatId);
                return unit;
            },
            async () =>
            {
                // No active question, generate new one
                await GenerateNewQuestion(chatId);
                return unit;
            }
        );
    }

    private async Task GenerateNewQuestion(long chatId)
    {
        // Generate new question
        var quizData = await quizService.GenerateQuizQuestion();

        await quizData.BindAsync(data => quizService.SendQuizToChat(chatId, data.Question, data.Answer).ToAsync())
            .Match(question =>
            {
                Log.Information("Quiz question sent to chat {ChatId}: {QuestionId}", chatId, question.Id.QuestionId);
                return unit;
            }, () =>
            {
                Log.Error("Failed to send quiz question to chat {ChatId}", chatId);
                return unit;
            });
    }
}