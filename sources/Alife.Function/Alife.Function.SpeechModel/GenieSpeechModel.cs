using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Platform;
using Python.Runtime;

namespace Alife.Function.Speech;

[Plugin("Genie语音合成", "基于GPT-SoVITS的本地离线语音合成引擎",
defaultCategory: "Alife 官方/模型接入/语音模型",
EditorUI = typeof(GenieSpeechModelUI))]
public class GenieSpeechModel :
    ISpeechModel,
    IDisposable,
    IConfigurable<GenieSpeechModelConfig>
{
    public static string RuntimeFolder => Path.Combine(AlifePath.RuntimeFolderPath, "Genie");

    public GenieSpeechModelConfig? Configuration { get; set; }

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

        string safeFileName = $"genie_{Configuration!.CharacterName}_{md5Hash}.wav";
        string outputPath = Path.Combine(AlifePath.TempFolderPath, safeFileName);

        if (File.Exists(outputPath))
            return outputPath;

        return await Task.Run(() => {
            using (Py.GIL())
            {
                dynamic synthesize = pythonModule!.GetAttr("synthesize");
                dynamic result = synthesize(
                    new PyString(text),
                    new PyString(outputPath),
                    new PyString(Configuration!.CharacterName)
                );

                string status = result["status"];
                if (status == "ok")
                {
                    string resultPath = result["result"];
                    if (File.Exists(resultPath))
                        return resultPath;
                }

                string message = result["message"];
                AlifeTerminal.LogWarning($"Genie synthesis failed: {message}");
                return null;
            }
        }, cancellationToken);
    }

    readonly PyModule? pythonModule;
    readonly string pythonCode =
        """"
        import os, json, traceback

        CHARA_VERSION = "v2ProPlus"
        OFFICIAL_CHARAS = {"feibi", "mika", "thirtyseven"}

        def init(model_path, chara_name, language):
            from huggingface_hub import snapshot_download

            # 基础资源路径（必须在 import genie_tts 之前设置）
            genie_data_dir = os.path.join(model_path, 'GenieData')
            os.environ['GENIE_DATA_DIR'] = genie_data_dir

            # 检查 GenieData 是否完整，不完整则下载
            hubert_dir = os.path.join(genie_data_dir, 'chinese-hubert-base')
            sv_model = os.path.join(genie_data_dir, 'speaker_encoder.onnx')
            if not os.path.exists(hubert_dir) or not os.path.exists(sv_model):
                print("GenieData 资源不完整，正在从 HuggingFace 下载...")
                snapshot_download(
                    repo_id="High-Logic/Genie",
                    repo_type="model",
                    allow_patterns="GenieData/*",
                    local_dir=model_path,
                )
            else:
                print("GenieData 资源已存在，跳过下载。")

            import genie_tts as genie

            # 角色查找：本地 → 官方（下载）→ 失败
            chara_dir = os.path.join(model_path, 'CharacterModels', CHARA_VERSION, chara_name)

            if not os.path.exists(chara_dir):
                if chara_name in OFFICIAL_CHARAS:
                    print(f"角色 '{chara_name}' 本地不存在，从 HuggingFace 下载...")
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

            # 加载模型
            model_dir = os.path.join(chara_dir, 'tts_models')
            genie.load_character(chara_name, model_dir, language)

            # 设置参考音频
            prompt_json = os.path.join(chara_dir, 'prompt_wav.json')
            with open(prompt_json, 'r', encoding='utf-8') as f:
                prompt = json.load(f)['Normal']
            audio_path = os.path.join(chara_dir, 'prompt_wav', prompt['wav'])
            genie.set_reference_audio(chara_name, audio_path, prompt['text'], language)

            print(f"角色 '{chara_name}' 加载成功。")

        def synthesize(text, output_path, chara_name):
            import genie_tts as genie
            try:
                genie.tts(
                    character_name=chara_name,
                    text=text,
                    save_path=output_path
                )
                return {'status': 'ok', 'result': output_path}
            except Exception as e:
                return {'status': 'error', 'message': traceback.format_exc()}
        """";

    public GenieSpeechModel()
    {
        string modelPath = RuntimeFolder;
        string charaName = Configuration?.CharacterName ?? "feibi";
        string language = Configuration?.Language ?? "Chinese";

        AlifePlatform.Command("python", "-m pip install genie-tts");

        using (Py.GIL())
        {
            pythonModule = Py.CreateScope(nameof(GenieSpeechModel));
            pythonModule.Exec(pythonCode);
            pythonModule.GetAttr("init").Invoke(
                new PyString(modelPath),
                new PyString(charaName),
                new PyString(language)
            );
        }
    }

    public void Dispose()
    {
        using (Py.GIL())
        {
            pythonModule?.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
