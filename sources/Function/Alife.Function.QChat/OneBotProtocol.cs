using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.QChat;

#region 协议枚举

[JsonConverter(typeof(JsonStringEnumConverter<OneBotPostType>))]
public enum OneBotPostType
{
    [JsonPropertyName("message")] Message,
    [JsonPropertyName("message_sent")] MessageSent,
    [JsonPropertyName("notice")] Notice,
    [JsonPropertyName("request")] Request,
    [JsonPropertyName("meta_event")] MetaEvent
}

[JsonConverter(typeof(JsonStringEnumConverter<OneBotMessageType>))]
public enum OneBotMessageType
{
    [JsonPropertyName("private")] Private,
    [JsonPropertyName("group")] Group
}

[JsonConverter(typeof(JsonStringEnumConverter<OneBotMetaType>))]
public enum OneBotMetaType
{
    [JsonPropertyName("lifecycle")] Lifecycle,
    [JsonPropertyName("heartbeat")] Heartbeat
}

#endregion

#region 事件多态模型

[JsonPolymorphic(TypeDiscriminatorPropertyName = "post_type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(OneBotMessageEvent), "message")]
[JsonDerivedType(typeof(OneBotMessageEvent), "message_sent")]
[JsonDerivedType(typeof(OneBotMetaEvent), "meta_event")]
[JsonDerivedType(typeof(OneBotNoticeEvent), "notice")]
[JsonDerivedType(typeof(OneBotRequestEvent), "request")]
public abstract record OneBotBaseEvent
{
    [JsonPropertyName("time")]
    public long Time { get; init; }

    [JsonPropertyName("self_id")]
    public long SelfId { get; init; }

    [JsonPropertyName("post_type")]
    public OneBotPostType PostType { get; init; }
}

public record OneBotMessageEvent : OneBotBaseEvent
{
    [JsonPropertyName("message_type")]
    public OneBotMessageType MessageType { get; init; }

    [JsonPropertyName("user_id")]
    public long UserId { get; init; }

    [JsonPropertyName("group_id")]
    public long GroupId { get; init; }

    [JsonPropertyName("message")]
    public object? Message { get; init; }

    [JsonPropertyName("raw_message")]
    public string RawMessage { get; init; } = "";
}

public record OneBotMetaEvent : OneBotBaseEvent
{
    [JsonPropertyName("meta_event_type")]
    public OneBotMetaType MetaEventType { get; init; }

    [JsonPropertyName("sub_type")]
    public string? SubType { get; init; }

    [JsonPropertyName("status")]
    public JsonElement? Status { get; init; }

    [JsonPropertyName("interval")]
    public long Interval { get; init; }
}

public record OneBotNoticeEvent : OneBotBaseEvent
{
    [JsonPropertyName("notice_type")]
    public string? NoticeType { get; init; }
    
    [JsonPropertyName("user_id")]
    public long UserId { get; init; }
    
    [JsonPropertyName("group_id")]
    public long GroupId { get; init; }
}

public record OneBotRequestEvent : OneBotBaseEvent
{
    [JsonPropertyName("request_type")]
    public string? RequestType { get; init; }
}

#endregion

#region API 通讯包

public record OneBotAction
{
    [JsonPropertyName("action")]
    public string Action { get; init; } = "";

    [JsonPropertyName("params")]
    public object? Params { get; init; }

    [JsonPropertyName("echo")]
    public string? Echo { get; init; }
}

public record SendMessageParams
{
    [JsonPropertyName("user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long UserId { get; init; }

    [JsonPropertyName("group_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long GroupId { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

public record UploadFileParams
{
    [JsonPropertyName("user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long UserId { get; init; }

    [JsonPropertyName("group_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long GroupId { get; init; }

    [JsonPropertyName("file")]
    public string File { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

#endregion
