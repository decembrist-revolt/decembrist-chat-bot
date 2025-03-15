using Amazon.S3;
using System.Net;
using Amazon.S3.Model;

namespace DecembristChatBotSharp.S3;

public class S3Service(AppConfig config, AmazonS3Client client, CancellationToken cancelToken)
{
    public async Task<bool> CheckConnection()
    {
        var bucketName = config.GetS3Config().BucketName;
        var request = new ListObjectsV2Request
        {
            BucketName = bucketName,
            MaxKeys = 1
        };
        var listResponse = await client.ListObjectsV2Async(request, cancelToken);
        return listResponse.HttpStatusCode == HttpStatusCode.OK;
    }
    
    public async Task<bool> DownloadDB()
    {
        var bucketName = config.GetS3Config().BucketName;
        using var @object = await client.GetObjectAsync(bucketName, config.DatabaseFile, cancelToken);
        if (@object.HttpStatusCode != HttpStatusCode.OK) return false;
        await using var fileStream = new FileStream(config.DatabaseFile, FileMode.Create, FileAccess.Write);
        await @object.ResponseStream.CopyToAsync(fileStream);

        return true;
    }
    
    public async Task<Try<PutObjectResponse>> UploadDB()
    {
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), config.DatabaseFile);
        if (!File.Exists(filePath)) throw new Exception("Database file not found");
        
        var bucketName = config.GetS3Config().BucketName;
        var putRequest = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = config.DatabaseFile,
            InputStream = File.OpenRead(filePath),
            Headers =
            {
                ["x-amz-content-sha256"] = "UNSIGNED-PAYLOAD"
            }
        };
        return await TryAsync(client.PutObjectAsync(putRequest, cancelToken));
    }
}