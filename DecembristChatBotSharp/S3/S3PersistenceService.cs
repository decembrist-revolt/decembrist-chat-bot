using Serilog;

namespace DecembristChatBotSharp.S3;

public class S3PersistenceService(AppConfig appConfig, S3Service s3Service, CancellationToken cancelToken)
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task Start()
    {
        if (!await _semaphore.WaitAsync(-1, cancelToken)) return;

        var lagSeconds = appConfig.PersistentConfig.PersistenceLagSeconds;
        await Task.Delay(TimeSpan.FromSeconds(lagSeconds), cancelToken);

        var tryUpload = await s3Service.UploadDB();
        tryUpload.Match(
            _ => Log.Information("Uploaded DB to S3"),
            ex => Log.Error(ex, "Failed to upload DB to S3")
        );
    }
}