using EmbedIO;
using EmbedIO.Actions;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Http;

[Singleton]
public class HealthCheckServer(AppConfig appConfig, CancellationTokenSource cancelToken)
{
    private bool _ready = false;

    public bool Ready
    {
        set => _ready = value;
    }

    public async void Start() => await Try(async () =>
    {
        var (port, host) = appConfig.HttpConfig;
        var url = $"http://{host}:{port}/";

        using var server = CreateWebServer(url);
        await server.RunAsync(cancelToken.Token);
    }).IfFail(ex =>
    {
        Log.Error(ex, "Failed during health check server");
        throw ex;
    });
    
    private WebServer CreateWebServer(string url) => new WebServer(o => o
                .WithUrlPrefix(url)
                .WithMode(HttpListenerMode.EmbedIO))
            .WithModule(new ActionModule(async ctx => ctx.Response.StatusCode = ctx.Request.RawUrl switch
            {
                "/health/live" => 200,
                "/health/ready" => _ready ? 200 : 400,
                _ => 404
            }));
}