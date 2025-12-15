using DecembristChatBotSharp.Service;
using Lamar;
using Quartz;
using Serilog;

namespace DecembristChatBotSharp.Scheduler;

[Singleton]
public class QuizValidatorJob(
    AppConfig appConfig,
    QuizService quizService) : IRegisterJob
{
    public async Task Register(IScheduler scheduler)
    {
        if (appConfig.QuizConfig is not { Enabled: true })
        {
            Log.Information("Quiz feature is disabled, skipping QuizValidatorJob registration");
            return;
        }

        var triggerKey = new TriggerKey(nameof(QuizValidatorJob));

        var job = JobBuilder.Create<QuizValidatorJob>()
            .WithIdentity(nameof(QuizValidatorJob))
            .Build();

        // Run every minute
        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .StartNow()
            .WithSimpleSchedule(x => x
                .WithIntervalInMinutes(1)
                .RepeatForever()
                .WithMisfireHandlingInstructionNextWithExistingCount())
            .Build();

        var existingTrigger = await scheduler.GetTrigger(triggerKey);

        if (existingTrigger != null)
        {
            await scheduler.RescheduleJob(triggerKey, trigger);
            Log.Information("QuizValidatorJob rescheduled to run every minute");
        }
        else
        {
            await scheduler.ScheduleJob(job, trigger);
            Log.Information("QuizValidatorJob registered to run every minute");
        }
    }

    public async Task Execute(IJobExecutionContext context)
    {
        Log.Debug("QuizValidatorJob started");
        await quizService.ProcessPendingAnswers();
        Log.Debug("QuizValidatorJob completed");
    }
}

