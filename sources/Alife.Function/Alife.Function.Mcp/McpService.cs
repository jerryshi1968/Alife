using System.Collections.Generic;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Alife.Function.Mcp;

public class McpServerConfig
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "Unnamed MCP Server";
    public string Description { get; set; } = "";
    public string Command { get; set; } = "";
    public string[] Arguments { get; set; } = [];
    public bool IsImplicit { get; set; } = true;
}

public class McpModuleConfig
{
    public List<McpServerConfig> Servers { get; set; } = new();
}

[Module("MCP服务", "让AI可以通过Model Context Protocol接入外部工具。",
    defaultCategory: "Alife 官方/功能底座",
    editorUI: typeof(McpServiceUI))]
public class McpService(XmlFunctionCaller functionService, ILoggerFactory loggerFactory)
    : InteractiveModule<McpService>, IConfigurable<McpModuleConfig>
{
    public McpModuleConfig? Configuration { get; set; }

    readonly List<McpClient> mcpClients = new();
    readonly List<XmlHandler> xmlHandlers = new();

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        foreach (McpServerConfig server in Configuration!.Servers)
        {
            if (server.Enabled == false) continue;

            (McpClient client, XmlHandler handler) = await McpXmlAdapter.CreateAsync(
                server,
                (name, result) => Poke($"{server.Name}.{name} 执行完成\n{result}"),
                loggerFactory
            );

            mcpClients.Add(client);
            xmlHandlers.Add(handler);

            functionService.RegisterHandler(handler, server.IsImplicit ? DocumentMode.Implicit : DocumentMode.Explicit);
        }
    }

    public override async Task DestroyAsync()
    {
        foreach (XmlHandler handler in xmlHandlers)
            functionService.UnregisterHandler(handler);
        xmlHandlers.Clear();

        foreach (McpClient client in mcpClients)
            await client.DisposeAsync();
        mcpClients.Clear();

        await base.DestroyAsync();
    }
}
