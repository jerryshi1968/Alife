using System;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.PythonPipe;
using Alife.Platform;

namespace Alife.Function.Vision;

/// <summary>
/// 使用 Qwen2.5-VL-3B-Instruct 进行图像理解。
/// 通过 PythonPipeProcess 子进程调用，避免 Python.NET 的 GIL 竞争。
/// </summary>
[Plugin("Qwen视觉分析", "基于Qwen2.5-VL的本地视觉分析引擎",
defaultCategory: "Alife 官方/模型接入/视觉模型",
EditorUI = typeof(QwenVisionModelUI))]
public class QwenVisionModel : IVisionModel,
    IAsyncDisposable,
    IDisposable,
    IConfigurable<QwenVisionModelConfig>
{
    public QwenVisionModelConfig? Configuration
    {
        get => new();
        set {}
    }

    readonly Lazy<Task<PythonPipeProcess>> pipeLazy;

    public event Action<string>? OnStderr;

    public QwenVisionModel()
    {
        pipeLazy = new Lazy<Task<PythonPipeProcess>>(CreatePipeAsync);
    }

    public async Task<string> QueryAsync(string imagePath, string question, int maxResponseTokens,
        CancellationToken cancellationToken = default)
    {
        try
        {
            PythonPipeProcess pipe = await pipeLazy.Value;
            return await pipe.InvokeAsync<string>("query",
                new object[] { new { image_path = imagePath, question, max_new_tokens = maxResponseTokens } },
                cancellationToken);
        }
        catch (Exception ex)
        {
            return $"调用失败：{ex}";
        }
    }

    async Task<PythonPipeProcess> CreatePipeAsync()
    {
        const string ModelId = "Qwen/Qwen2.5-VL-3B-Instruct";
        string modelPath = AlifeModel.EnsureModelExisting(ModelId);
        AlifePlatform.Command("python", "-m pip install torch torchvision Pillow transformers qwen-vl-utils bitsandbytes accelerate sentencepiece tiktoken");

        PythonPipeProcess pipe = new("qwen_vl", pythonCode, pythonExe: null);
        pipe.OnStderr += line => OnStderr?.Invoke(line);
        await pipe.StartAsync();

        await pipe.InvokeAsync<string>("init", modelPath);
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
        import sys, json, torch
        from PIL import Image
        from transformers import Qwen2_5_VLForConditionalGeneration, AutoProcessor, BitsAndBytesConfig
        from qwen_vl_utils import process_vision_info

        device = torch.device('cuda')
        model = None
        processor = None

        def init(model_path):
            global model, processor
            quantization_config = BitsAndBytesConfig(
                load_in_4bit=True,
                bnb_4bit_compute_dtype=torch.bfloat16,
                bnb_4bit_use_double_quant=True,
                bnb_4bit_quant_type="nf4"
            )
            model = Qwen2_5_VLForConditionalGeneration.from_pretrained(
                model_path,
                dtype="auto",
                quantization_config=quantization_config,
                device_map="auto",
                attn_implementation="sdpa"
            )
            processor = AutoProcessor.from_pretrained(
                model_path,
                min_pixels=256 * 28 * 28,
                max_pixels=512 * 28 * 28
            )
            return "ready"

        def query(image_path, question, max_new_tokens):
            image = Image.open(image_path).convert("RGB")
            messages = [
                {
                    "role": "user",
                    "content": [
                        {"type": "image", "image": image},
                        {"type": "text", "text": question},
                    ],
                }
            ]
            text = processor.apply_chat_template(
                messages, tokenize=False, add_generation_prompt=True
            )
            image_inputs, video_inputs = process_vision_info(messages)
            inputs = processor(
                text=[text],
                images=image_inputs,
                videos=video_inputs,
                padding=True,
                return_tensors="pt",
            )
            inputs = inputs.to(device)
            with torch.no_grad():
                generated_ids = model.generate(**inputs, max_new_tokens=max_new_tokens)
                generated_ids_trimmed = [
                    out_ids[len(in_ids):] for in_ids, out_ids in zip(inputs.input_ids, generated_ids)
                ]
                res = processor.batch_decode(
                    generated_ids_trimmed, skip_special_tokens=True, clean_up_tokenization_spaces=False
                )
            del inputs, generated_ids
            torch.cuda.empty_cache()
            return res[0].strip()
        """;
}
