using System.Text.Json.Serialization;
using Peek.Ipc.Protocol;

namespace Peek.Ipc;

[JsonSerializable(typeof(JsonRpcRequest))]
[JsonSerializable(typeof(JsonRpcResponse))]
[JsonSerializable(typeof(JsonRpcError))]
[JsonSerializable(typeof(GetElementFromPointParams))]
[JsonSerializable(typeof(GetElementFromHandleParams))]
[JsonSerializable(typeof(GetChildrenParams))]
[JsonSerializable(typeof(ElementInfo))]
[JsonSerializable(typeof(ElementRect))]
[JsonSerializable(typeof(WorkerStatus))]
[JsonSerializable(typeof(nint))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class IpcJsonContext : JsonSerializerContext { }