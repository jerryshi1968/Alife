using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.PythonPipe;
using Alife.Platform;

namespace Alife.Function.Speech;

[Plugin("VITS语音合成", "基于VITS的本地离线语音合成引擎",
defaultCategory: "Alife 官方/模型接入/语音模型",
EditorUI = typeof(VitsSpeechModelUI))]
public class VitsSpeechModel :
    ISpeechModel,
    IAsyncDisposable,
    IDisposable,
    IConfigurable<VitsSpeechModelConfig>
{
    public static string RuntimeFolder => Path.Combine(AlifePath.RuntimeFolderPath, "VITS");

    public VitsSpeechModelConfig? Configuration { get; set; }

    public event Action<string>? OnStderr;

    readonly Lazy<Task<PythonPipeProcess>> pipeLazy;

    public VitsSpeechModel()
    {
        pipeLazy = new Lazy<Task<PythonPipeProcess>>(CreatePipeAsync);
    }

    public async Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string md5Hash;
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
            md5Hash = Convert.ToHexString(hashBytes);
        }

        string safeFileName = $"vits_{Configuration!.SpeakerId}_{md5Hash}.wav";
        string outputPath = Path.Combine(AlifePath.TempFolderPath, safeFileName);

        if (File.Exists(outputPath))
            return outputPath;

        try
        {
            PythonPipeProcess pipe = await pipeLazy.Value;
            return await pipe.InvokeAsync<string>("synthesize", text, outputPath,
                Configuration.SpeakerId, Configuration.NoiseScale,
                Configuration.NoiseScaleW, Configuration.LengthScale);
        }
        catch (Exception ex)
        {
            return $"调用失败：{ex}";
        }
    }

    async Task<PythonPipeProcess> CreatePipeAsync()
    {
        string vitsDir = RuntimeFolder;
        AlifePlatform.Command("python", $"-m pip install -r {Path.Combine(vitsDir, "requirements.txt").Replace(Path.DirectorySeparatorChar, '/')}");

        PythonPipeProcess pipe = new("vits_speech", pythonCode, pythonExe: null);
        pipe.OnStderr += line => OnStderr?.Invoke(line);
        await pipe.StartAsync();

        await pipe.InvokeAsync<string>("init", vitsDir);
        return pipe;
    }

    public async ValueTask DisposeAsync()
    {
        if (pipeLazy.IsValueCreated)
        {
            PythonPipeProcess pipe = await pipeLazy.Value;
            await pipe.DisposeAsync();
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    readonly string pythonCode =
        """
        # coding=utf-8
        import sys, os, json, traceback
        import numpy as np
        import torch
        import wave
        from torch import no_grad, LongTensor

        vits_dir = None

        def init(vits_path):
            global vits_dir
            vits_dir = vits_path
            if vits_dir not in sys.path:
                sys.path.insert(0, vits_dir)
            from models import SynthesizerTrn
            from text import text_to_sequence
            import commons
            import utils

            global hps_ms, net_g_ms, device
            device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
            hps_ms = utils.get_hparams_from_file(f'{vits_dir}/model/config.json')
            net_g_ms = SynthesizerTrn(
                len(hps_ms.symbols),
                hps_ms.data.filter_length // 2 + 1,
                hps_ms.train.segment_size // hps_ms.data.hop_length,
                n_speakers=hps_ms.data.n_speakers,
                **hps_ms.model)
            _ = net_g_ms.eval().to(device)
            utils.load_checkpoint(f'{vits_dir}/model/G_953000.pth', net_g_ms, None)
            return "ready"

        def get_text(text, hps):
            from text import text_to_sequence
            import commons
            text_norm, clean_text = text_to_sequence(text, hps.symbols, hps.data.text_cleaners)
            if hps.data.add_blank:
                text_norm = commons.intersperse(text_norm, 0)
            text_norm = LongTensor(text_norm)
            return text_norm, clean_text

        def vits(text, language, speaker_id, noise_scale, noise_scale_w, length_scale):
            text = text.replace('\n', ' ').replace('\r', '').replace(' ', '')
            if language == 0:
                text = f'[ZH]{text}[ZH]'
            elif language == 1:
                text = f'[JA]{text}[JA]'
            stn_tst, _ = get_text(text, hps_ms)
            with no_grad():
                x_tst = stn_tst.unsqueeze(0).to(device)
                x_tst_lengths = LongTensor([stn_tst.size(0)]).to(device)
                sid = LongTensor([speaker_id]).to(device)
                audio = net_g_ms.infer(
                    x_tst, x_tst_lengths, sid=sid,
                    noise_scale=noise_scale,
                    noise_scale_w=noise_scale_w,
                    length_scale=length_scale
                )[0][0, 0].data.cpu().float().numpy()
            return 22050, audio

        def synthesize(text, output_path, speaker_id=0, noise_scale=0.6, noise_scale_w=0.668, length_scale=1.2):
            sr, audio = vits(text, 0, speaker_id, noise_scale, noise_scale_w, length_scale)
            audio_int16 = (audio * 32767).astype(np.int16)
            with wave.open(output_path, 'wb') as wf:
                wf.setnchannels(1)
                wf.setsampwidth(2)
                wf.setframerate(sr)
                wf.writeframes(audio_int16.tobytes())
            return output_path
        """;
}
