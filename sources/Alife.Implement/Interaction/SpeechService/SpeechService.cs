using Alife.Basic;
using System.ComponentModel;
using System.Diagnostics;
using Alife.Framework;
using Alife.Function.Interpreter;
using Alife.Function.Speech;
using Microsoft.SemanticKernel;
using NAudio.CoreAudioApi;

namespace Alife.Implement;

[Plugin("语音对话", "为AI增加语音识别和语音转文字输出的能力。", ConfigurationUIType = typeof(SpeechServiceUI))]
[Description("此服务让你获得能将文字以语音形式输出的能力。")]
public class SpeechService : InteractivePlugin<SpeechService>, IAsyncDisposable, ITimeIterative
{
    public static SpeechRecognizer ActiveRecognizer => Recognizer;
    public static SpeechSynthesizer ActiveSynthesizer => Synthesizer;
    [XmlFunction("say", -10)]
    [Description("使用语音的方式向用户发送消息。")]
    public async Task Say(XmlExecutorContext context, [XmlContent] string content)
    {
        if (context.CallMode == CallMode.Reset)
        {
            if (Synthesizer.IsSpeaking)
                await Synthesizer.StopSpeakAsync(); //中断语音

            return;
        }

        if (hasHeadphones == false)
        {
            if (context.CallMode == CallMode.Opening)
            {
                //当没有耳机播放音频时，需要关闭语音识别，避免冲突
                if (Recognizer.IsRecognizing)
                    Recognizer.Stop();
            }
            else if (context.CallMode == CallMode.Closing)
            {
                //当停止说话时，等待当前语音结束后，恢复语音识别
                if (Synthesizer.IsSpeaking)
                    await Synthesizer.LastSpeaking;
                if (Recognizer.IsRecognizing == false)
                    Recognizer.Start();
            }
        }

        content = content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        //收到新的语音播报任务，先进行语音合成
        audioFileSynthesizingCancellation = new CancellationTokenSource();
        Task<string?> audioSynthesizingTask = Synthesizer.GenerateSpeechFileAsync(content, audioFileSynthesizingCancellation.Token);
        //如果当前有音频在播放，则等待占用结束
        if (Synthesizer.IsSpeaking)
        {
            try
            {
                await Synthesizer.LastSpeaking;
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
            Terminal.LogWarning(e.ToString());
        }

        if (audioFile == null)
            return; //计算后发现没有可朗读的文本

        //不等待播放任务，继续接收下一次函数调用，从而实现预加载
        _ = Synthesizer.SpeakAudioAsync(audioFile).ContinueWith(_ => {
            try
            {
                //播放完成后，尝试删除语音
                File.Delete(audioFile);
            }
            catch (Exception e)
            {
                Terminal.LogWarning(e.ToString());
            }
        });
    }

    static readonly SpeechRecognizer Recognizer;
    static readonly SpeechSynthesizer Synthesizer;
    static SpeechService()
    {
        Recognizer = new SpeechRecognizer();
        Synthesizer = new SpeechSynthesizer();
    }

    readonly MMDeviceEnumerator enumerator = new();

    CancellationTokenSource? audioFileSynthesizingCancellation;
    bool hasHeadphones;

    public SpeechService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);
        Recognizer.Recognized += OnRecognized;
    }
    public async ValueTask DisposeAsync()
    {
        Recognizer.Recognized -= OnRecognized;
        if (Synthesizer.IsSpeaking) //等待语音说完
            await Synthesizer.LastSpeaking;
    }
    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);

        //打开语音识别
        if (Recognizer.IsRecognizing == false)
            Recognizer.Start();
    }
    public void Update(ref int time)
    {
        MMDevice? device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        hasHeadphones = device.FriendlyName.Contains("耳机") ||
                        device.FriendlyName.Contains("Headphones") ||
                        device.FriendlyName.Contains("Headset") ||
                        device.FriendlyName.Contains("Earphone");

        if (hasHeadphones && Recognizer.IsRecognizing == false)
        {
            Recognizer.Start();
            SendNotification("语音输入常驻开启", "检测到耳机，已通过 SpeechService 开启实时识别。");

            void SendNotification(string title, string message)
            {
                try
                {
                    string script = $"$Title='{title}'; $Message='{message}'; " +
                                    "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null; " +
                                    "$Template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); " +
                                    "$TextNodes = $Template.GetElementsByTagName('text'); " +
                                    "$TextNodes.Item(0).AppendChild($Template.CreateTextNode($Title)) | Out-Null; " +
                                    "$TextNodes.Item(1).AppendChild($Template.CreateTextNode($Message)) | Out-Null; " +
                                    "$Toast = [Windows.UI.Notifications.ToastNotification]::new($Template); " +
                                    "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('AlifeSpeechAssist').Show($Toast);";

                    Process.Start(new ProcessStartInfo {
                        FileName = "powershell",
                        Arguments = $"-Command \"{script}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Notification failed: {ex.Message}");
                }
            }
        }
    }
    void OnRecognized(string text)
    {
        Chat(text);
    }
}
