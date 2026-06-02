using Peek.Ipc.Connection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Peek.Ipc.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPeekIpc(
        this IServiceCollection services,
        Action<WorkerConnectionOptions>? configure = null)
    {
        services.TryAddSingleton<WorkerConnectionOptions>(_ =>
        {
            var builder = new WorkerConnectionOptionsBuilder();
            configure?.Invoke(builder.Options);

            var envPath = Environment.GetEnvironmentVariable("Peek_WORKER_PATH");
            if (!string.IsNullOrWhiteSpace(envPath))
                builder.Options = builder.Options with
                {
                    WorkerExecutablePath = envPath
                };

            return builder.Options;
        });

        services.TryAddSingleton<WorkerConnection>();

        return services;
    }
}

internal sealed class WorkerConnectionOptionsBuilder
{
    public WorkerConnectionOptions Options { get; set; } = new();
}
