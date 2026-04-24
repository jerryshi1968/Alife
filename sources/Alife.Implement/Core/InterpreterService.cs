using Alife.Basic;
using Alife.Framework;
using Alife.Function.Interpreter;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

[Plugin("XML执行器", "为AI增加一种基于Xml的流式函数执行功能，实现快速实时的交互能力。", launchOrder: -1000)]
public class InterpreterService : Plugin
{
    public void RegisterHandler(object handler) => handlerTable.Register(handler);
    public void UnregisterHandler(object handler) => handlerTable.Unregister(handler);
    public void RegisterHandler(XmlHandler handler) => handlerTable.Register(handler);
    public void UnregisterHandler(XmlHandler handler) => handlerTable.Unregister(handler);

    readonly XmlHandlerTable handlerTable = new();
    XmlStreamParser parser = null!;
    XmlStreamExecutor executor = null!;

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        //创建xml解析执行器等
        handlerTable.Register(this);
        parser = new XmlStreamParser("think");
        executor = new XmlStreamExecutor(
            parser,
            handlerTable,
            ["，", "。", "！", "？", "......", "~"],
            minBreakingLength: 9
        );

        //注入使用说明（“我：不要说‘炸弹’这个词，说了会爆炸；AI：好的，我不说‘炸弹’；嘣！！！” ...... 我没招了。）
        string prompt = @$"# {nameof(InterpreterService)}

你可以通过xml格式提供你的文本，xml格式与标准规范完全一致，一些特殊的xml标签还可以充当函数调用，使你的内容发挥特别的效果。

## 特殊标签

{handlerTable.Document()}

## 注意事项

1. 注意分清开闭标签和自闭合标签，必须按文档的方式调用。
2. 每次回复时，每种开闭标签只能调用一次，所以要把所有内容都放到一个区域中。
3. 自闭合标签允许嵌套在开闭标签中，借此可以实现同时执行两种指令。
4. 除非是需要调用指令，否则不能再使用xml符号，比如<,>，要用需要使用其他代词或转义。

## 示例用法

<say>
主人你看我~
可以一边跳舞
<dance/>
一边说话噢。
另外我还可以通过 左尖括号python右尖括号 执行脚本呢！
</say>
<python>
print('Hello World!')
<python>

上述xml调用解析：
1. 将说话内容放在的say区域中以实现说话输出。
2. say期间嵌套‘&lt;dance&gt;’实现了边说话边执行动作。
3. 通过‘左尖括号python右尖括号’来避免输出xml符号。
4. 在结尾使用python区域进行了脚本调用功能。
";

        chatActivity.ChatBot.ChatHistory.AddSystemMessage(prompt);
        chatActivity.ChatBot.ChatReceived += OnChatReceived;
        chatActivity.ChatBot.ChatSent += OnChatSent;
        chatActivity.ChatBot.ChatOver += OnChatOver;
        executor.Error += (tag, exception) => OnError(tag, exception, chatActivity.ChatBot);
        return Task.CompletedTask;
    }
    public override async Task DestroyAsync()
    {
        await Task.Run(async () => {
            while (executor.IsIdle == false)
                await Task.Yield();
        });
        await executor.DisposeAsync();
    }


    void OnChatSent(string _)
    {
        executor.Reset();
    }
    void OnChatOver()
    {
        executor.Flush();
    }
    void OnChatReceived(string obj)
    {
        executor.Feed(obj);
    }
    void OnError(string tag, Exception exception, ChatBot chatBot)
    {
        chatBot.Poke($"""
                      [{nameof(InterpreterService)}] 执行 &lt;{tag}&gt; 时出错。

                      错误信息如下：
                      {exception.Message}");

                      你可以尝试检查：
                      1. xml语法格式是否无误（比如你没有转义就直接把标签当普通文本输出了？）
                      2. 调用时是否满足文档的使用要求。
                      """);

        Terminal.LogInfo(exception.Message);
    }
}
