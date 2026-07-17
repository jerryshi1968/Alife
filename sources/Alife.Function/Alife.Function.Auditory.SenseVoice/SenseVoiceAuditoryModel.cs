using System;
using System.Diagnostics;
using System.IO;
using SherpaOnnx;
using Alife.Framework;
using Alife.Function.AIModelUtility;
using Alife.Function.Auditory;
using Microsoft.Extensions.Logging;

namespace Alife.Function.Auditory.SenseVoice;

[Module("SenseVoice语音识别", "基于SenseVoice的本地语音识别引擎",
    defaultCategory: "Alife 官方/模型接入/听觉模型",
    EditorUI = typeof(SenseVoiceAuditoryModelUI))]
public class SenseVoiceAuditoryModel :
    IAuditoryModel,
    IDisposable,
    IConfigurable<SenseVoiceAuditoryModelConfig>
{
    public static bool ModelsExists
    {
        get
        {
            string senseVoicePath = Path.Combine(Alife.Function.AIModelUtility.AIModelUtility.ModelScopeModelPath, SenseVoiceId.Replace(".", "___"));
            string vadPath = Path.Combine(Alife.Function.AIModelUtility.AIModelUtility.ModelScopeModelPath, VadId.Replace(".", "___"));
            return File.Exists(Path.Combine(senseVoicePath, "model.int8.onnx"))
                   && File.Exists(Path.Combine(vadPath, "silero_vad.onnx"));
        }
    }

    public SenseVoiceAuditoryModelConfig? Configuration
    {
        get => configuration;
        set
        {
            configuration = value ?? new SenseVoiceAuditoryModelConfig();
            RecreateModels();
        }
    }

    public event Action<string>? Recognized;
    public void AcceptWaveform(float[] samples)
    {
        var detector = vad;
        if (detector == null)
            return;
        lock (detector)
        {
            detector.AcceptWaveform(samples);
            while (detector.IsEmpty() == false)
            {
                SpeechSegment segment = detector.Front();
                if (segment.Samples is { Length: > 0 })
                    ProcessSegment(segment.Samples);
                detector.Pop();
            }
        }
    }

    const string SenseVoiceId = "pengzhendong/sherpa-onnx-sense-voice-zh-en-ja-ko-yue";
    const string VadId = "pengzhendong/silero-vad";
    readonly string senseVoicePath;
    readonly string vadModelPath;
    readonly ILogger<SenseVoiceAuditoryModel> logger;
    SenseVoiceAuditoryModelConfig configuration = new();
    OfflineRecognizer? recognizer;
    VoiceActivityDetector? vad;

    void ProcessSegment(float[] samples)
    {
        var currentRecognizer = recognizer;
        if (currentRecognizer == null)
            return;

        Stopwatch stopwatch = Stopwatch.StartNew();
        using OfflineStream stream = currentRecognizer.CreateStream();
        stream.AcceptWaveform(16000, samples);
        currentRecognizer.Decode(stream);
        stopwatch.Stop();

        string text = stream.Result.Text;
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (text == "。")
            return;
        logger.LogInformation("[Perf][ASR] segment recognized duration={DurationMs:F0}ms decode={DecodeMs}ms textLength={TextLength} text={Text}", samples.Length / 16f, stopwatch.ElapsedMilliseconds, text.Length, text);
        Recognized?.Invoke(text);
    }

    public SenseVoiceAuditoryModel(ILogger<SenseVoiceAuditoryModel> logger)
    {
        this.logger = logger;
        senseVoicePath = Alife.Function.AIModelUtility.AIModelUtility.EnsureModelExisting(SenseVoiceId);
        vadModelPath = Alife.Function.AIModelUtility.AIModelUtility.EnsureModelExisting(VadId, "silero_vad.onnx");
        RecreateModels();
    }

    void RecreateModels()
    {
        recognizer?.Dispose();
        vad?.Dispose();

        OfflineRecognizerConfig config = new();
        config.ModelConfig.SenseVoice.Model = Path.Combine(senseVoicePath, "model.int8.onnx");
        config.ModelConfig.SenseVoice.Language = configuration.Language;
        config.ModelConfig.SenseVoice.UseInverseTextNormalization = configuration.UseInverseTextNormalization ? 1 : 0;
        config.ModelConfig.Tokens = Path.Combine(senseVoicePath, "tokens.txt");
        config.ModelConfig.NumThreads = configuration.NumThreads;
        config.ModelConfig.Debug = 0;
        recognizer = new OfflineRecognizer(config);

        VadModelConfig vadConfig = new();
        vadConfig.SileroVad.Model = vadModelPath;
        vadConfig.SileroVad.Threshold = configuration.VadThreshold;
        vadConfig.SileroVad.MinSilenceDuration = configuration.VadMinSilenceDuration;
        vadConfig.SileroVad.MinSpeechDuration = configuration.VadMinSpeechDuration;
        vadConfig.SampleRate = 16000;
        vad = new VoiceActivityDetector(vadConfig, bufferSizeInSeconds: 30);
        logger.LogInformation("[Perf][ASR] VAD configured threshold={Threshold} minSilence={MinSilence}s minSpeech={MinSpeech}s threads={Threads}", configuration.VadThreshold, configuration.VadMinSilenceDuration, configuration.VadMinSpeechDuration, configuration.NumThreads);
    }
    public void Dispose()
    {
        recognizer?.Dispose();
        vad?.Dispose();
        GC.SuppressFinalize(this);
    }
}
