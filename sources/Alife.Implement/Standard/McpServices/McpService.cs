using Alife.Framework;
using Alife.Function.Interpreter;
using ModelContextProtocol.Client;

namespace Alife.Implement;

public class McpServerConfig
{
    public string Name { get; set; } = "Unnamed MCP Server";
    public string Description { get; set; } = "";
    public string Command { get; set; } = "";
    public string[] Arguments { get; set; } = [];
}
public class McpPluginConfig
{
    public List<McpServerConfig> Servers { get; set; } = new();
}
[Plugin("MCP服务", "让AI可以通过Model Context Protocol接入外部工具。",
    configurationUIType: typeof(McpServiceUI))]
public class McpService : InteractivePlugin<McpService>, IConfigurable<McpPluginConfig>
{
    public McpPluginConfig? Configuration { get; set; }

    readonly List<McpClient> mcpClients = new();
    readonly List<XmlHandler> xmlHandlers = new();
    readonly InterpreterService interpreterService;

    public McpService(InterpreterService interpreterService)
    {
        this.interpreterService = interpreterService;
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        foreach (McpServerConfig server in Configuration!.Servers)
        {
            (McpClient client, XmlHandler handler) = await McpXmlAdapter.CreateAsync(
                server,
                (name, result) => Poke($"{server.Name}.{name} 执行完成\n{result}"));

            mcpClients.Add(client);
            xmlHandlers.Add(handler);
            interpreterService.RegisterHandler(handler);
        }
    }
    public override async Task DestroyAsync()
    {
        foreach (XmlHandler handler in xmlHandlers)
            interpreterService.UnregisterHandler(handler);
        xmlHandlers.Clear();

        foreach (McpClient client in mcpClients)
            await client.DisposeAsync();
        mcpClients.Clear();

        await base.DestroyAsync();
    }
}
