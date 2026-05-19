using System.ComponentModel;
using Alife.Framework;
using Alife.Function.Interpreter;
using Alife.Function.Speech;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

public class SpeechConfig
{
    public string VoiceTone { get; set; } = "zh-CN-XiaoyiNeural";
}

public partial class SpeechService
{
    public static bool IsRecognizing => recognizer is { IsRecognizing: true };

    static SpeechRecognizer? recognizer;

    static void TryInitializedAsync()
    {
        recognizer ??= new SpeechRecognizer();
        _ = Task.Run(async () => {
            try
            {
                while (true)//持续更新麦克风状态
                {
                    await Task.Delay(2000);
                    if (recognizer.IsInitialized == false)
                        await recognizer.TryInitializeAudioAsync();
                    if (recognizer.IsInitialized && recognizer.IsRecognizing == false)
                        recognizer.Start();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        });
    }
}

[Plugin("语音对话", "为AI增加语音识别（基于本地模型）和语音转文字输出（基于edge-tts）的能力。", EditorUI = typeof(SpeechServiceUI))]
[Description("此服务让你获得能将文字以语音形式输出的能力。")]
public partial class SpeechService(FunctionService functionService)
    : InteractivePlugin<SpeechService>, IAsyncDisposable, IConfigurable<SpeechConfig>
{
    [XmlFunction(FunctionMode.Content, order: -10)]
    [Description("将文本以语音方式输出。")]
    public async Task Speak(XmlExecutorContext context, [XmlContent] string content, CancellationToken cancellationToken)
    {
        try
        {
            switch (context.CallMode)
            {
                case CallMode.Opening:
                    try
                    {
                        if (synthesizer!.IsSpeaking)
                            await synthesizer.LastSpeaking;
                    }
                    catch (OperationCanceledException) {}
                    break;
                case CallMode.Closing:
                    break;
                case CallMode.Content:
                {
                    content = content.Trim();
                    if (string.IsNullOrWhiteSpace(content))
                        break;
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    await synthesizer!.SpeakAsync(content, cancellationToken);
                    break;
                }
            }
        }
        catch (OperationCanceledException) {}
    }

    public SpeechConfig? Configuration
    {
        get => configuration;
        set
        {
            configuration = value;
            if (configuration != null && synthesizer != null)
                synthesizer.VoiceTone = configuration.VoiceTone;
        }
    }

    public bool IsSynthesizing => synthesizer?.IsSpeaking ?? false;
    public bool IsReceiving { get; set; } = true;

    protected override string ChatPrefixPrompt => "[语音识别的信息，请用Speak回复]";
    SpeechSynthesizerBase? synthesizer;
    SpeechConfig? configuration;

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        TryInitializedAsync();//语音识别
        synthesizer = new SpeechSynthesizer(Configuration!.VoiceTone);//语音合成

        functionService.RegisterHandler(this);
    }
    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);
        recognizer!.Recognized += OnRecognized;//开始接收语音识别
    }
    public override async Task DestroyAsync()
    {
        recognizer!.Recognized -= OnRecognized;
        await base.DestroyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (synthesizer != null)
            await synthesizer.LastSpeaking;
    }

    void OnRecognized(string text)
    {
        if (IsReceiving)
            Chat(text);
    }
}
