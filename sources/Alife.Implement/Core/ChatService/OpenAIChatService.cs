using Alife.Framework;
using Microsoft.SemanticKernel;
using System.Net;
using Alife.Plugins.Official.Components;

namespace Alife.Implement;

public class OpenAIChatServiceConfig : ICloneable
{
    public string endpoint = "";
    public string modelId = "";
    public string apiKey = "";

    public object Clone()
    {
        return new OpenAIChatServiceConfig() {
            endpoint = endpoint,
            modelId = modelId,
            apiKey = apiKey
        };
    }
}
[Plugin(
    "LLM对话能力", "基于OpenAI协议的对话模型功能接入。",
    url: "https://www.deepseek.com/",
    configurationUIType: typeof(OpenAIChatServiceUI)
)]
public class OpenAIChatService : Plugin, IConfigurable<OpenAIChatServiceConfig>
{
    public OpenAIChatServiceConfig? Configuration { get; set; }
    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        // 强制使用 HTTP 1.1 以解决某些提供者（如 DeepSeek）在流式传输时可能出现的 HttpIOException
        HttpClient httpClient = new(new SocketsHttpHandler {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
                RemoteCertificateValidationCallback = delegate { return true; }
            },
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        }) {
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        context.kernelBuilder.AddOpenAIChatCompletion(
            endpoint: new Uri(Configuration!.endpoint),
            modelId: Configuration!.modelId,
            apiKey: Configuration!.apiKey,
            httpClient: httpClient
        );
    }
}
