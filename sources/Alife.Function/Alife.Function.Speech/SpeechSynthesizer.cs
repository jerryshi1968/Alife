using System.Diagnostics;
using Alife.Basic;
using NAudio.Wave;

namespace Alife.Function.Speech;

public class SpeechSynthesizer : SpeechSynthesizerBase
{
    public override bool IsSpeaking => currentTask is { IsCompleted: false };
    public override Task LastSpeaking => currentTask;
    public override string VoiceTone { get => voiceTone; set => voiceTone = value; }
    public override async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        //收到新的语音播报任务，先进行语音合成
        audioSynthesizingTask = GenerateSpeechFileAsync(text, cancellationToken);
        //如果当前有音频在播放，则等待占用结束
        if (IsSpeaking)
        {
            try
            {
                await LastSpeaking;
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
        _ = SpeakFromFileAsync(audioFile, cancellationToken).ContinueWith(_ => {
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
    }
    
    public SpeechSynthesizer(string voiceTone)
    {
        AlifePlatform.Command("python", "-m pip install --upgrade edge-tts");

        invalidChars = Path.GetInvalidFileNameChars();
        this.voiceTone = voiceTone;
        currentTask = Task.CompletedTask;
    }

    // 裁剪开头和结尾静音
    class SilenceTrimmer : ISampleProvider
    {
        public WaveFormat WaveFormat { get; }

        public SilenceTrimmer(ISampleProvider source, float threshold = 0.01f)
        {
            WaveFormat = source.WaveFormat;

            List<float> allSamples = new();
            float[] tempBuffer = new float[WaveFormat.SampleRate];
            int read;
            while ((read = source.Read(tempBuffer, 0, tempBuffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                    allSamples.Add(tempBuffer[i]);
            }

            int start = 0;
            while (start < allSamples.Count && Math.Abs(allSamples[start]) <= threshold)
                start++;

            int end = allSamples.Count - 1;
            while (end > start && Math.Abs(allSamples[end]) <= threshold)
                end--;

            if (start <= end)
            {
                int length = end - start + 1;
                samples = new float[length];
                allSamples.CopyTo(start, samples, 0, length);
            }
            else
            {
                samples = [];
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int available = samples.Length - position;
            int toCopy = Math.Min(available, count);
            if (toCopy > 0)
            {
                samples.AsSpan(position, toCopy).CopyTo(buffer.AsSpan(offset, toCopy));
                position += toCopy;
            }

            return toCopy;
        }

        readonly float[] samples;
        int position;
    }

    readonly char[] invalidChars;
    string voiceTone;
    Task currentTask;
    CancellationTokenSource? speakCancellation;
    Task<string?> audioSynthesizingTask = Task.FromResult<string?>(null);

    /// <summary>
    /// 播放指定位置的音频文件
    /// </summary>
    async Task PlayAudioAsync(string filePath, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource tcs = new();

        AudioFileReader reader = new(filePath);
        WaveOutEvent speaker = new();
        speaker.Init(new SilenceTrimmer(reader));
        speaker.PlaybackStopped += OnPlaybackStopped;
        speaker.Play();

        //在取消时停止播放
        await using CancellationTokenRegistration registration = cancellationToken.Register(() => speaker.Stop());

        await tcs.Task;
        await Task.Delay(200, cancellationToken);

        void OnPlaybackStopped(object? _, StoppedEventArgs e)
        {
            if (e.Exception != null)
                tcs.SetException(e.Exception);
            else
                tcs.SetResult();
        }
    }

    /// <summary>
    /// 不进行语音合成，直接将已存在的音频文件作为说话内容，借助该函数，可以将合成与播放分离，从而实现预加载等功能。
    /// </summary>
    async Task SpeakFromFileAsync(string file, CancellationToken cancellationToken = default)
    {
        if (IsSpeaking)
            await StopSpeakAsync();

        speakCancellation = new CancellationTokenSource();
        using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, speakCancellation.Token);
        currentTask = PlayAudioAsync(file, cancellationTokenSource.Token);

        await currentTask;
    }

    Task StopSpeakAsync()
    {
        if (IsSpeaking == false)
            throw new InvalidOperationException("当前没有语音中。");

        return speakCancellation!.CancelAsync();
    }

    /// <summary>
    /// 通过edge-tts生成音频文件
    /// </summary>
    async Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
    {
        //计算输出位置
        string fileSafeText = string.Concat(text.Where(ch => invalidChars.Contains(ch) == false));
        if (string.IsNullOrWhiteSpace(fileSafeText))
            return null;
        string outputPath = Path.Combine(Path.GetTempPath(), fileSafeText + ".mp3");
        if (File.Exists(outputPath))
            return outputPath;

        ProcessStartInfo psi = new() {
            FileName = "python",
            Arguments = $"-m edge_tts --text \"{fileSafeText}\" --voice {voiceTone} --write-media \"{outputPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using Process? process = Process.Start(psi);
        if (process == null)
            return null;

        try
        {
            // Console.WriteLine("开始合成音频：" + text);

            // 在线语音合成可能因为网络或拒绝服务导致卡死，需要处理超时问题
            await Task.WhenAny(
            process.WaitForExitAsync(cancellationToken),
            Task.Delay(5000, cancellationToken));
            if (process.HasExited == false)
                throw new TimeoutException();
            if (process.ExitCode != 0)
                throw new Exception(
                $"{outputPath}\n{await process.StandardOutput.ReadToEndAsync(cancellationToken)}\n{await process.StandardError.ReadToEndAsync(cancellationToken)}");
            if (File.Exists(outputPath) == false)
                throw new Exception($"语音文件未生成：{outputPath}");

            return outputPath;
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            if (process.HasExited == false)
                process.Kill();

            // Console.WriteLine("音频合成完成：" + text);
        }
    }
}
