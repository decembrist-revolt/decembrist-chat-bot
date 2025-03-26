using System.Collections.Specialized;
using Lamar;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Impl;
using Quartz.Spi.MongoDbJobStore;
using static Quartz.Impl.StdSchedulerFactory;

namespace DecembristChatBotSharp.DI;

public static class QuartzConfiguration
{
    public static void AddQuartz(this ServiceRegistry registry)
    {
        registry.AddSingleton(GetScheduler);
    }

    private static IScheduler GetScheduler(IServiceProvider serviceProvider)
    {
        var appConfig = serviceProvider.GetRequiredService<AppConfig>();
        var jobFactory = serviceProvider.GetRequiredService<LamarJobFactory>();
        var connectionString = appConfig.MongoConfig.ConnectionString;
        var properties = new NameValueCollection
        {
            [PropertySchedulerInstanceName] = "ChatBot",
            [PropertySchedulerInstanceId] = $"{Environment.MachineName}-{Guid.NewGuid()}",
            [PropertyJobStoreType] = typeof(MongoDbJobStore).AssemblyQualifiedName,
            [$"{PropertyJobStorePrefix}.{PropertyDataSourceConnectionString}"] = connectionString,
            [$"{PropertyJobStorePrefix}.collectionPrefix"] = "Quartz",
            ["quartz.serializer.type"] = "json",
        };

        var scheduler = new StdSchedulerFactory(properties).GetScheduler().Result;
        scheduler.JobFactory = jobFactory;
        return scheduler;
    }
}