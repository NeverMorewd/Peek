
using Peek.Ipc.Channel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Peek.Ipc.Protocol;

public sealed class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; } = "2.0";

    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Params { get; init; }
}

public sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public int? Id { get; init; }

    [JsonPropertyName("result")]
    public System.Text.Json.JsonElement? Result { get; init; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; init; }

    [JsonIgnore]
    public bool IsSuccess => Error is null && Result.HasValue;
}

public sealed class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}


public static class RpcErrorCodes
{
    public const int ParseError      = -32700;
    public const int InvalidRequest  = -32600;
    public const int MethodNotFound  = -32601;
    public const int InvalidParams   = -32602;
    public const int InternalError   = -32603;
    public const int ElementNotFound = -32001;
    public const int UiaError        = -32002;
    public const int Timeout         = -32003;
}


public sealed class GetElementFromPointParams
{
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
}

public sealed class GetElementFromHandleParams
{
    [JsonPropertyName("hwnd")] public nint Hwnd { get; init; }
}

public sealed class GetChildrenParams
{
    [JsonPropertyName("hwnd")]  public nint Hwnd  { get; init; }
    [JsonPropertyName("depth")] public int? Depth { get; init; }
}

public sealed class ElementInfo
{
    [JsonPropertyName("name")]                 
    public string Name                { get; init; } = "None";
    [JsonPropertyName("control_type")]         
    public string ControlType         { get; init; } = "None";
    [JsonPropertyName("automation_id")]        
    public string AutomationId        { get; init; } = string.Empty;
    [JsonPropertyName("class_name")]           
    public string ClassName           { get; init; } = "None";
    [JsonPropertyName("process_id")]           
    public uint   ProcessId           { get; init; }
    [JsonPropertyName("framework")]            
    public string Framework           { get; init; } = string.Empty;
    [JsonPropertyName("rect")]                 
    public ElementRect Rect           { get; init; } = new();
    [JsonPropertyName("is_enabled")]           
    public bool   IsEnabled           { get; init; }
    [JsonPropertyName("is_keyboard_focusable")]
    public bool   IsKeyboardFocusable { get; init; }
    [JsonPropertyName("hwnd")]
    [JsonConverter(typeof(IntPtrConverter))]
    public nint   Hwnd                { get; init; }

    [JsonIgnore]
    public string Abstraction
    {
        get
        {
            return $"[{Name}]-[{ControlType}]-[{ClassName}]";
        }
    }

    public override bool Equals(object? other)
    {
        if (other is ElementInfo otherInfo)
        {
            return Hwnd == otherInfo.Hwnd
            && ProcessId == otherInfo.ProcessId
            && ControlType == otherInfo.ControlType
            && ClassName == otherInfo.ClassName
            && Name == otherInfo.Name;
        }
        else 
            return false;
    }
    public override int GetHashCode()
    {
        var hash = Hwnd.GetHashCode();
        hash = HashCode.Combine(hash, ProcessId);
        hash = HashCode.Combine(hash, ControlType);
        hash = HashCode.Combine(hash, ClassName);
        hash = HashCode.Combine(hash, Name);
        return hash;
    }
}

public sealed class ElementRect
{
    [JsonPropertyName("left")]   
    public int Left   { get; init; }
    [JsonPropertyName("top")]    
    public int Top    { get; init; }
    [JsonPropertyName("width")]  
    public int Width  { get; init; }
    [JsonPropertyName("height")] 
    public int Height { get; init; }

    public override string ToString()
    {
        return $"X={Left};Y={Top};Width={Width};Height={Height}";
    }
}

public sealed class WorkerStatus
{
    [JsonPropertyName("version")]        public string Version       { get; init; } = string.Empty;
    [JsonPropertyName("uptime_secs")]    public ulong  UptimeSecs    { get; init; }
    [JsonPropertyName("queries_served")] public ulong  QueriesServed { get; init; }
    [JsonPropertyName("cache_hits")]     public ulong  CacheHits     { get; init; }
    [JsonPropertyName("cache_misses")]   public ulong  CacheMisses   { get; init; }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}
