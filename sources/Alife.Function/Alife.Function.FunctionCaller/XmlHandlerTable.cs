using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Interpreter;

public class XmlHandlerTable
{
    public IReadOnlyList<XmlHandler> Handlers => xmlHandlers;

    public void Register(XmlHandler handler)
    {
        xmlHandlers.Add(handler);
        foreach (XmlFunction xmlFunction in handler.Functions)
        {
            if (xmlFunctions.TryGetValue(xmlFunction.Name, out SortedSet<XmlFunction>? xmlFunctionGroup) == false)
            {
                xmlFunctionGroup = new SortedSet<XmlFunction>();
                xmlFunctions[xmlFunction.Name] = xmlFunctionGroup;
            }

            xmlFunctionGroup.Add(xmlFunction);
        }
    }

    public void Unregister(XmlHandler handler)
    {
        xmlHandlers.Remove(handler);
        foreach (XmlFunction xmlHandlerFunction in handler.Functions)
        {
            if (xmlFunctions.TryGetValue(xmlHandlerFunction.Name, out SortedSet<XmlFunction>? xmlFunctionGroup))
                xmlFunctionGroup.Remove(xmlHandlerFunction);
        }
    }

    public string Document()
    {
        StringBuilder sb = new();
        foreach (XmlHandler handler in xmlHandlers)
        {
            if (handler.IsImplicit)
                continue;
            sb.AppendLine(handler.Document());
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public async Task Handle(string name, XmlContext tagContext, CancellationToken cancellationToken = default)
    {
        SortedSet<XmlFunction>? xmlFunctionGroup = xmlFunctions.GetValueOrDefault(name.ToLower());
        if (xmlFunctionGroup == null || xmlFunctionGroup.Count == 0)
            throw new Exception($"未找到名为 {name} 的可调用函数");
        foreach (XmlFunction xmlFunction in xmlFunctionGroup)
            await xmlFunction.Invoker(tagContext, cancellationToken);
    }

    readonly List<XmlHandler> xmlHandlers = new();
    readonly Dictionary<string, SortedSet<XmlFunction>> xmlFunctions = new();
}
