
using Peek.Ipc.Protocol;

namespace Peek.Ipc.Channel;

public sealed class RpcException : Exception
{
    public int Code    { get; }
    public string RpcMessage { get; }

    public RpcException(JsonRpcError error)
        : base($"RPC error {error.Code}: {error.Message}")
    {
        Code       = error.Code;
        RpcMessage = error.Message;
    }

    public bool IsElementNotFound => Code == RpcErrorCodes.ElementNotFound;

    public bool IsUiaError => Code == RpcErrorCodes.UiaError;
}
