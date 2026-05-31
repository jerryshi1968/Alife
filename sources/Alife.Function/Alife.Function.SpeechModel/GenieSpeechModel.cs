using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.PythonPipe;
using Alife.Platform;

namespace Alife.Function.Speech;

[Plugin("Genie语音合成", "基于GPT-SoVITS的本地离线语音合成引擎",
defaultCategory: "Alife 官方/模型接入/语音模型",
EditorUI = typeof(GenieSpeechModelUI))]
public class GenieSpeechModel :
    ISpeechModel,
    IAsyncDisposable,
    IDisposable,
    IConfigurable<GenieSpeechModelConfig>
{
    public static string RuntimeFolder => Path.Combine(AlifePath.RuntimeFolderPath, "Genie");

    public GenieSpeechModelConfig? Configuration { get; set; }

    public event Action<string>? OnStderr;

    readonly Lazy<Task<PythonPipeProcess>> pipeLazy;

    public GenieSpeechModel()
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

        string charaName = Configuration!.CharacterName;
        string safeFileName = $"genie_{charaName}_{md5Hash}.wav";
        string outputPath = Path.Combine(AlifePath.TempFolderPath, safeFileName);

        if (File.Exists(outputPath))
            return outputPath;

        try
        {
            PythonPipeProcess pipe = await pipeLazy.Value;
            await pipe.InvokeAsync<string>("synthesize", text, outputPath, charaName);
            return outputPath;
        }
        catch (Exception ex)
        {
            OnStderr?.Invoke($"Genie synthesis failed: {ex.Message}");
            return null;
        }
    }

    async Task<PythonPipeProcess> CreatePipeAsync()
    {
        string modelPath = RuntimeFolder;
        string charaName = Configuration?.CharacterName ?? "feibi";
        string language = Configuration?.Language ?? "Chinese";

        AlifePlatform.Command("python", "-m pip install genie-tts");

        PythonPipeProcess pipe = new("genie_speech", pythonCode, pythonExe: null);
        pipe.OnStderr += line => OnStderr?.Invoke(line);
        await pipe.StartAsync();

        await pipe.InvokeAsync<string>("init", modelPath, charaName, language);
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
        import os, json

        CHARA_VERSION = "v2ProPlus"
        OFFICIAL_CHARAS = {"feibi", "mika", "thirtyseven"}

        def init(model_path, chara_name, language):
            from huggingface_hub import snapshot_download

            genie_data_dir = os.path.join(model_path, 'GenieData')
            os.environ['GENIE_DATA_DIR'] = genie_data_dir

            hubert_dir = os.path.join(genie_data_dir, 'chinese-hubert-base')
            sv_model = os.path.join(genie_data_dir, 'speaker_encoder.onnx')
            if not os.path.exists(hubert_dir) or not os.path.exists(sv_model):
                print("GenieData 资源不完整，正在从 HuggingFace 下载...", flush=True)
                snapshot_download(
                    repo_id="High-Logic/Genie",
                    repo_type="model",
                    allow_patterns="GenieData/*",
                    local_dir=model_path,
                )

            import genie_tts as genie

            chara_dir = os.path.join(model_path, 'CharacterModels', CHARA_VERSION, chara_name)
            if not os.path.exists(chara_dir):
                if chara_name in OFFICIAL_CHARAS:
                    print(f"角色 '{chara_name}' 本地不存在，从 HuggingFace 下载...", flush=True)
                    snapshot_download(
                        repo_id="High-Logic/Genie",
                        repo_type="model",
                        allow_patterns=f"CharacterModels/{CHARA_VERSION}/{chara_name}/*",
                        local_dir=model_path,
                    )
                else:
                    raise FileNotFoundError(
                        f"角色 '{chara_name}' 不存在于本地，也不是官方预定义角色。"
                        f"请将角色模型放入 {chara_dir}"
                    )

            model_dir = os.path.join(chara_dir, 'tts_models')
            genie.load_character(chara_name, model_dir, language)

            prompt_json = os.path.join(chara_dir, 'prompt_wav.json')
            with open(prompt_json, 'r', encoding='utf-8') as f:
                prompt = json.load(f)['Normal']
            audio_path = os.path.join(chara_dir, 'prompt_wav', prompt['wav'])
            genie.set_reference_audio(chara_name, audio_path, prompt['text'], language)

            print(f"角色 '{chara_name}' 加载成功。", flush=True)
            return "ready"

        def synthesize(text, output_path, chara_name):
            import genie_tts as genie
            genie.tts(
                character_name=chara_name,
                text=text,
                save_path=output_path
            )
            return output_path
        """;
}
