using Alife.Basic;
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

                    //收到新的语音播报任务，先进行语音合成
                    audioSynthesizingTask = synthesizer!.GenerateSpeechFileAsync(content, cancellationToken);
                    //如果当前有音频在播放，则等待占用结束
                    if (synthesizer.IsSpeaking)
                    {
                        try
                        {
                            await synthesizer.LastSpeaking;
                        }
                        catch (OperationCanceledException)
                        {
                            return;//语音被打断，那么后续语音显然也不用播放了
                        }
                    }

                    //可以播放音频
                    string? audioFile = null;
                    try
                    {
                        audioFile = await audioSynthesizingTask;//等待合成任务完成
                    }
                    catch (Exception e)
                    {
                        //因为输入文本和网络原因，合成并不一定成功，但基本稳定，大部分错误都是难以处理的，所以直接忽略即可
                        AlifeTerminal.LogWarning(e.ToString());
                    }

                    if (audioFile == null)
                        return;//计算后发现没有可朗读的文本

                    //不等待播放任务，继续接收下一次函数调用，从而实现预加载
                    _ = synthesizer.SpeakAudioAsync(audioFile, cancellationToken).ContinueWith(_ => {
                        try
                        {
                            //播放完成后，尝试删除语音
                            File.Delete(audioFile);
                        }
                        catch (Exception e)
                        {
                            AlifeTerminal.LogWarning(e.ToString());
                        }
                    }, cancellationToken);
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
    Task<string?> audioSynthesizingTask = Task.FromResult<string?>(null);
    SpeechSynthesizer? synthesizer;
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
        if (audioSynthesizingTask.IsCompleted == false)
            await audioSynthesizingTask;
        if (synthesizer != null)
            await synthesizer.LastSpeaking;
    }

    void OnRecognized(string text)
    {
        if (IsReceiving)
            Chat(text);
    }
}
