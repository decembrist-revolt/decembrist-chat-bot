using Quartz;

namespace DecembristChatBotSharp.Scheduler;

public interface IRegisterJob : IJob
{
    public Task Register(IScheduler scheduler);
}