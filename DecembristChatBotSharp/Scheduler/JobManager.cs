using Lamar;
using Quartz;

namespace DecembristChatBotSharp.Scheduler;

[Singleton]
public class JobManager(
    IScheduler scheduler, 
    IList<IRegisterJob> jobs,
    CancellationTokenSource cancelToken)
{
    public async Task Start()
    {
        await jobs.Map(job => job.Register(scheduler)).WhenAll();
        await scheduler.Start(cancelToken.Token);
    }
    
    public async Task Shutdown()
    {
        await scheduler.Shutdown(cancelToken.Token);
    }
}