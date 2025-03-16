using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;

namespace DecembristChatBotSharp.S3;

public static class S3Client
{
    public static AmazonS3Client GetInstance(IServiceProvider serviceProvider)
    {
        var appConfig = serviceProvider.GetRequiredService<AppConfig>();
        var s3Config = Optional(appConfig.PersistentConfig.S3Config).Match(
            identity,
            () => throw new Exception("S3Config is not set while persistent is enabled")
        );
        var config = new AmazonS3Config
        {
            ServiceURL = s3Config.ServiceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = s3Config.Region,
            LogResponse = true,
        };
        return new AmazonS3Client(s3Config.AccessKey, s3Config.SecretKey, config);
    }
    
    public static S3Config GetS3Config(this AppConfig appConfig) =>
        Optional(appConfig.PersistentConfig.S3Config)
            .Match(identity, () => throw new Exception("S3Config is not set"));
}