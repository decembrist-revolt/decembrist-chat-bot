using DecembristChatBotSharp.Service;
using Lamar;
using Quartz;
using Serilog;

namespace DecembristChatBotSharp.Scheduler;

[Singleton]
public class QuizValidatorJob(
    AppConfig appConfig,
    QuizService quizService,
    SchedulerService schedulerService) : IRegisterJob
{
    public TriggerKey TriggerKey => new(nameof(QuizValidatorJob));

    public async Task Register(IScheduler scheduler)
    {
        if (appConfig.QuizConfig is not { Enabled: true })
        {
            Log.Information("Quiz feature is disabled, skipping QuizValidatorJob registration");
            return;
        }

        var job = JobBuilder.Create<QuizValidatorJob>()
            .WithIdentity(nameof(QuizValidatorJob))
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(TriggerKey)
            .StartNow()
            .WithSimpleSchedule(x => x
                .WithIntervalInMinutes(1)
                .RepeatForever()
                .WithMisfireHandlingInstructionNextWithExistingCount())
            .Build();

        await schedulerService.RegisterOrRescheduleJob(scheduler, TriggerKey, job, trigger);
    }

    public async Task Execute(IJobExecutionContext context)
    {
        Log.Debug("QuizValidatorJob started");
        await quizService.ProcessPendingAnswers();
        Log.Debug("QuizValidatorJob completed");
    }
}

