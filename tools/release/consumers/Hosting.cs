using System.Collections.Concurrent;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

var probe = new ScopeProbe();
var services = new ServiceCollection();
services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
services.AddSingleton(probe);
services.AddScoped<IOutboxDispatcher<ConsumerDatabase>, Dispatcher>();
services.AddScoped<IInboxProcessor<ConsumerDatabase>, Processor>();
services.AddInboxHandler<ConsumerDatabase, Handler>("package-proof");
services.AddOutboxWorker<ConsumerDatabase>(
    new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10))
);
services.AddInboxWorker<ConsumerDatabase>(
    "package-proof",
    new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10))
);
await using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
);
var workers = provider.GetServices<IHostedService>().ToArray();
if (workers.Length != 2)
    throw new InvalidOperationException("Both explicitly selected workers must be registered.");

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
try
{
    foreach (var worker in workers)
        await worker.StartAsync(timeout.Token);

    await Task.WhenAll(probe.Dispatched.Task, probe.Processed.Task).WaitAsync(timeout.Token);
}
finally
{
    foreach (var worker in workers)
        await worker.StopAsync(timeout.Token);
}

Console.WriteLine(
    "PASS opt-in hosting packages: independent workers use fresh scopes without a database provider"
);

sealed class ConsumerDatabase : DbContext;

sealed class ScopeProbe
{
    private readonly ConcurrentDictionary<Guid, byte> _dispatchScopes = new();
    private readonly ConcurrentDictionary<Guid, byte> _processScopes = new();
    internal TaskCompletionSource Dispatched { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Processed { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Dispatch(Guid scope)
    {
        _dispatchScopes.TryAdd(scope, 0);
        if (_dispatchScopes.Count >= 2)
            Dispatched.TrySetResult();
    }

    internal void Process(Guid scope)
    {
        _processScopes.TryAdd(scope, 0);
        if (_processScopes.Count >= 2)
            Processed.TrySetResult();
    }
}

sealed class Dispatcher(ScopeProbe probe) : IOutboxDispatcher<ConsumerDatabase>
{
    private readonly Guid _scope = Guid.NewGuid();

    public Task<OutboxDispatchResult> DispatchNextAsync(
        CancellationToken cancellationToken = default
    )
    {
        probe.Dispatch(_scope);
        return Task.FromResult(OutboxDispatchResult.NoWork);
    }
}

sealed class Processor(ScopeProbe probe) : IInboxProcessor<ConsumerDatabase>
{
    private readonly Guid _scope = Guid.NewGuid();

    public Task<InboxProcessingResult> ProcessNextAsync(
        string subscriptionKey,
        CancellationToken cancellationToken = default
    )
    {
        if (subscriptionKey != "package-proof")
            throw new InvalidOperationException(
                "The worker must retain the selected subscription."
            );

        probe.Process(_scope);
        return Task.FromResult(InboxProcessingResult.NoWork);
    }
}

sealed class Handler : IInboxHandler<ConsumerDatabase>
{
    public Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The no-work processor must not invoke the handler.");
}
