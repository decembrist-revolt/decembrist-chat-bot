using DecembristChatBotSharp.Scheduler;
using Lamar;
using Quartz;
using Quartz.Spi;

namespace DecembristChatBotSharp.DI;

[Singleton]
public class LamarJobFactory(IList<IRegisterJob> jobs) : IJobFactory
{
    private readonly Dictionary<string, IRegisterJob> _schedulers = 
        jobs.ToDictionary(scheduler => GetTypeName(scheduler.GetType()));

    public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler) =>
        _schedulers.TryGetValue(GetTypeName(bundle.JobDetail.JobType), out var job) 
            ? job : throw new Exception("Job not found");

    public void ReturnJob(IJob job)
    {
    }

    private static string GetTypeName(Type type) => 
        type.FullName ?? throw new Exception($"Scheduler name {type} not found");
}