using Microsoft.SemanticKernel;
using System.Net;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Alife.Framework;

public class ChatServiceConfig : ICloneable
{
    public string endpoint = "";
    public string modelId = "";
    public string apiKey = "";
    public bool thinkingEnabled = true;
    public string reasoningEffort = "high";


    public object Clone()
    {
        return new ChatServiceConfig() {
            endpoint = endpoint,
            modelId = modelId,
            apiKey = apiKey,
            thinkingEnabled = thinkingEnabled,
            reasoningEffort = reasoningEffort
        };
    }
}

[Plugin(
"对话能力", "基于OpenAI协议的对话模型功能接入。",
url: "https://www.deepseek.com/",
editorUI: typeof(ChatServiceUI)
)]
public class ChatService : Plugin, IConfigurable<ChatServiceConfig>, IProvideExecutionSettings
{
    public ChatServiceConfig? Configuration { get; set; }
    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        // 强制使用 HTTP 1.1 以解决某些提供者（如 DeepSeek）在流式传输时可能出现的 HttpIOException
        SocketsHttpHandler handler = new SocketsHttpHandler {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
                RemoteCertificateValidationCallback = delegate { return true; }
            },
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        // 使用通用处理器拦截并破解所有 OpenAI 兼容协议的思考过程字段
        OpenAICompatibleHandler reasoningHandler = new(handler);

        HttpClient httpClient = new(reasoningHandler) {
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        context.KernelBuilder.AddOpenAIChatCompletion(
        endpoint: new Uri(Configuration!.endpoint),
        modelId: Configuration!.modelId,
        apiKey: Configuration!.apiKey,
        httpClient: httpClient
        );
    }
    
    public void ProvideSettings(OpenAIPromptExecutionSettings settings)
    {
        settings.ReasoningEffort = Configuration!.reasoningEffort;

        // 设置 DeepSeek 特有的思考模式参数 (通过 extra_body 传递)
        settings.ExtensionData ??= new Dictionary<string, object>();
        settings.ExtensionData["extra_body"] = new {
            thinking = new {
                type = "enabled"
            }
        };
    }
}
