using Quartz;

namespace DecembristChatBotSharp.Scheduler;

public interface IRegisterJob : IJob
{
    TriggerKey TriggerKey { get; }

    public Task Register(IScheduler scheduler);
}