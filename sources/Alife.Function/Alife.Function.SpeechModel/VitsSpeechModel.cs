using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.PythonPipe;
using Alife.Platform;
using Microsoft.Extensions.Logging;

namespace Alife.Function.Speech;

[Module("VITS语音合成", "基于VITS的本地离线语音合成引擎",
    defaultCategory: "Alife 官方/模型接入/语音模型",
    EditorUI = typeof(VitsSpeechModelUI))]
public class VitsSpeechModel(
    ILogger<VitsSpeechModel> logger
) :
    ISpeechModel,
    IAsyncDisposable,
    ISystemEvent,
    IConfigurable<VitsSpeechModelConfig>
{
    public static string RuntimeFolder => Path.Combine(AlifePath.RuntimeFolderPath, "VITS");
    public VitsSpeechModelConfig? Configuration { get; set; }

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
            return await pythonPipe!.InvokeAsync<string>("synthesize", text, outputPath,
                Configuration.SpeakerId, Configuration.NoiseScale,
                Configuration.NoiseScaleW, Configuration.LengthScale);
        }
        catch (Exception ex)
        {
            return $"调用失败：{ex}";
        }
    }

    PythonPipeProcess? pythonPipe;
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

    public async Task AwakeAsync(AwakeContext context)
    {
        if (Directory.Exists(RuntimeFolder) == false)
            await DownloadAndExtractAsync();

        pythonPipe = new("vits_speech", pythonCode);
        pythonPipe.OnStderr += line => logger.LogWarning(line);
        await pythonPipe.StartAsync();
        await pythonPipe.InvokeAsync<string>("init", RuntimeFolder);
    }
    public async ValueTask DisposeAsync()
    {
        if (pythonPipe != null)
        {
            await pythonPipe.DisposeAsync();
        }
    }

    async Task DownloadAndExtractAsync()
    {
        const string zipUrl = "https://github.com/BDFFZI/Alife/releases/download/VITS/VITS.zip";
        string zipPath = Path.Combine(AlifePath.TempFolderPath, "VITS.zip");

        logger.LogInformation("正在下载 VITS 模型文件...");

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, zipUrl);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        {
            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] buffer = new byte[81920];
            long readSoFar = 0;
            int bytesRead;
            long nextReport = 10 * 1024 * 1024;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                readSoFar += bytesRead;
                if (readSoFar >= nextReport)
                {
                    if (totalBytes > 0)
                        logger.LogInformation("下载进度: {Pct:F1}% ({ReadMB}MB / {TotalMB}MB)",
                            (double)readSoFar / totalBytes * 100,
                            readSoFar / 1024 / 1024,
                            totalBytes / 1024 / 1024);
                    else
                        logger.LogInformation("下载进度: {ReadMB}MB", readSoFar / 1024 / 1024);
                    nextReport = readSoFar + 10 * 1024 * 1024;
                }
            }
        }

        logger.LogInformation("下载完成，正在解压...");
        string extractRoot = AlifePath.RuntimeFolderPath;
        bool hasTopLevelVits;
        using (var archive = ZipFile.OpenRead(zipPath))
            hasTopLevelVits = archive.Entries.Any(e => e.FullName.StartsWith("VITS/", StringComparison.OrdinalIgnoreCase));
        if (hasTopLevelVits == false)
            extractRoot = Path.Combine(AlifePath.RuntimeFolderPath, "VITS");

        ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);
        File.Delete(zipPath);
        logger.LogInformation("VITS 模型文件准备就绪。");
    }
}
