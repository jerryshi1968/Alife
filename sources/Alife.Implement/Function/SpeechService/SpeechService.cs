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
    public static bool IsRecognizing => recognizer?.IsRecognizing ?? false;

    public static void TryStartRecognition()
    {
        if (recognizer is { IsRecognizing: false })
            recognizer.Start();
    }

    public static void TryStopRecognition()
    {
        if (recognizer is { IsRecognizing: true })
            recognizer.Stop();
    }

    static SpeechRecognizer? recognizer;

    static void TryInitialized()
    {
        recognizer ??= new SpeechRecognizer();
    }
}

[Plugin("语音对话", "为AI增加语音识别（基于本地模型）和语音转文字输出（基于edge-tts）的能力。", EditorUI = typeof(SpeechServiceUI))]
[Description("此服务让你获得能将文字以语音形式输出的能力。")]
public partial class SpeechService(FunctionService functionService)
    : InteractivePlugin<SpeechService>, IAsyncDisposable, ITimeIterative, IConfigurable<SpeechConfig>
{
    [XmlFunction(order: -10)]
    [Description("将文本以语音方式输出。")]
    public async Task Speak(XmlExecutorContext context, [XmlContent] string content)
    {
        if (context.CallMode == CallMode.Reset)
        {
            await TryStopSynthesizer();
            return;
        }

        if (hasHeadphones == false)
        {
            if (context.CallMode == CallMode.Opening)
                TryStopRecognition();
            else if (context.CallMode == CallMode.Closing)
            {
                //当停止说话时，等待当前语音结束后，恢复语音识别
                if (synthesizer!.IsSpeaking)
                    await synthesizer.LastSpeaking;
                TryStartRecognition();
            }
        }

        if (context.CallMode != CallMode.Content)
            return;
        content = content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        //收到新的语音播报任务，先进行语音合成
        audioFileSynthesizingCancellation = new CancellationTokenSource();
        audioSynthesizingTask = synthesizer!.GenerateSpeechFileAsync(content, audioFileSynthesizingCancellation.Token);
        //如果当前有音频在播放，则等待占用结束
        if (synthesizer.IsSpeaking)
        {
            try
            {
                await synthesizer.LastSpeaking;
            }
            catch (OperationCanceledException)
            {
                return; //语音被打断，那么后续语音显然也不用播放了
            }
        }


        //可以播放音频
        string? audioFile = null;
        try
        {
            audioFile = await audioSynthesizingTask; //等待合成任务完成
        }
        catch (Exception e)
        {
            //因为输入文本和网络原因，合成并不一定成功，但基本稳定，大部分错误都是难以处理的，所以直接忽略即可
            AlifeTerminal.LogWarning(e.ToString());
        }

        if (audioFile == null)
            return; //计算后发现没有可朗读的文本

        //不等待播放任务，继续接收下一次函数调用，从而实现预加载
        _ = synthesizer.SpeakAudioAsync(audioFile).ContinueWith(_ =>
        {
            try
            {
                //播放完成后，尝试删除语音
                File.Delete(audioFile);
            }
            catch (Exception e)
            {
                AlifeTerminal.LogWarning(e.ToString());
            }
        });
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
    public bool IsSpeaking => IsSynthesizing || audioSynthesizingTask.IsCompleted == false;
    public bool IsReceiving { get; set; } = true;

    public async Task TryStopSynthesizer()
    {
        if (synthesizer is { IsSpeaking : true })
            await synthesizer.StopSpeakAsync(); //中断语音
    }

    protected override string ChatPrefixPrompt => "[这是语音识别信息，建议使用Speak回复（注意！语音识别很可能误识别，要根据内容洞察真实情况）]";
    Task<string?> audioSynthesizingTask = Task.FromResult<string?>(null);
    CancellationTokenSource? audioFileSynthesizingCancellation;
    bool hasHeadphones;
    SpeechSynthesizer? synthesizer;
    SpeechConfig? configuration;

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        TryInitialized();
        synthesizer = new SpeechSynthesizer(Configuration!.VoiceTone);
        functionService.RegisterHandler(this);
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);
        //打开语音识别
        if (recognizer != null)
        {
            recognizer.Recognized += OnRecognized;
            TryStartRecognition();
        }
    }

    public override async Task DestroyAsync()
    {
        if (recognizer != null)
            recognizer.Recognized -= OnRecognized;

        await base.DestroyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (IsSpeaking)
        {
            if (audioSynthesizingTask.IsCompleted == false)
                await audioSynthesizingTask;
            if (synthesizer != null)
                await synthesizer.LastSpeaking;
        }
    }

    public void OnUpdate(ref float time)
    {
        hasHeadphones = SpeechEnvironment.HasHeadphones();
        if (hasHeadphones) TryStartRecognition();
    }

    void OnRecognized(string text)
    {
        if (IsReceiving)
            Chat(text);
    }
}