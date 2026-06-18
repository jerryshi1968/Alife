using Alife.Platform;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Speech;
using Alife.Function.Speech.VITS;


// VitsSpeechSynthesizer speechSynthesizer = new VitsSpeechSynthesizer();
// Console.WriteLine(await speechSynthesizer.GenerateSpeechFileAsync("喵…又重启了喵…真央回来了喵…主人还在忙吗喵…真央趁这个空档偷偷去网上逛了逛喵…主人猜真央看到了什么有趣的喵…"));


Character character = new() {
    Name = "SpeechTest",
    Prompt = "用<speak>说话",
    Modules = [
        typeof(OpenAILanguageModel).FullName!,
        typeof(XmlFunctionCaller).FullName!,
        typeof(SpeechService).FullName!,
        typeof(VitsSpeechModel).FullName!
    ]
};

DemoSuite suite = await DemoSuite.InitializeAsync(character, system => {
    system.SetConfiguration(typeof(VitsSpeechModel), new VitsSpeechModelConfig() {
        SpeakerId = 551,
        NoiseScale = 0.6f,
        NoiseScaleW = 0.668f,
        LengthScale = 1.2f
    }, character.StorageKey);
});

AlifeTerminal.Log("----------------------------------------");

await suite.RunAsync();
