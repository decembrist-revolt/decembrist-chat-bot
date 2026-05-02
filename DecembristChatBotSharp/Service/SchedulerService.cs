using Lamar;
using Quartz;
using Serilog;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class SchedulerService
{
    public async Task RegisterOrRescheduleJob(
        IScheduler scheduler,
        TriggerKey triggerKey,
        IJobDetail jobDetail,
        ITrigger trigger)
    {
        var existingTrigger = await scheduler.GetTrigger(triggerKey);

        if (existingTrigger != null)
        {
            await scheduler.RescheduleJob(triggerKey, trigger);
            Log.Information("Job {JobName} rescheduled", jobDetail.Key.Name);
        }
        else
        {
            await scheduler.ScheduleJob(jobDetail, trigger);
            Log.Information("Job {JobName} registered", jobDetail.Key.Name);
        }
    }
}

