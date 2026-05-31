namespace Alife.Function.Speech;

public class AuditoryServiceConfig
{
    public string ResultPrefixPrompt { get; set; } = "[语音识别信息，请用<speak>回复]";
    public string? PushToTalkKey { get; set; }
}
