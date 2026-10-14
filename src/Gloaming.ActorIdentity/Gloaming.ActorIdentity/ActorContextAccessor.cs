namespace Gloaming.ActorIdentity;

/// <summary>
/// A scope-owned context holder. Consumers establish it before starting business work
/// and dispose it after all operation branches finish. Reads are safe to perform concurrently.
/// </summary>
public sealed class ActorContextAccessor
    : IActorContextAccessor,
        IActorContextInitializer,
        IDisposable
{
    private readonly object _gate = new();
    private ActorContext? _context;
    private bool _disposed;

    public ActorContext Current
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _context
                    ?? throw new InvalidOperationException(
                        "Actor context has not been initialized."
                    );
            }
        }
    }

    public void Initialize(ActorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_context is not null)
            {
                throw new InvalidOperationException("Actor context has already been initialized.");
            }

            _context = context;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _context = null;
        }

        GC.SuppressFinalize(this);
    }
}
