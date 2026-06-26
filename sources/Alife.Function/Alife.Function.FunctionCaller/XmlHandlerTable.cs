using System;
using System.Collections.Generic;
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

            foreach (XmlParameter parameter in xmlFunction.Parameters)
            {
                if (parameter.IsXmlForm)
                    xmlForms.Add(parameter.Name.ToLower());
            }
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

    public async Task Handle(string name, XmlContext tagContext, CancellationToken cancellationToken = default)
    {
        name = name.ToLower();
        SortedSet<XmlFunction>? xmlFunctionGroup = xmlFunctions.GetValueOrDefault(name);
        if (xmlFunctionGroup == null || xmlFunctionGroup.Count == 0)
        {
            if (xmlForms.Contains(name))
                return;
            throw new Exception($"当前环境没有{name}函数，请停止使用");
        }
        foreach (XmlFunction xmlFunction in xmlFunctionGroup)
            await xmlFunction.Invoker(tagContext, cancellationToken);
    }

    readonly List<XmlHandler> xmlHandlers = new();
    readonly Dictionary<string, SortedSet<XmlFunction>> xmlFunctions = new();
    readonly HashSet<string> xmlForms = new();
}
