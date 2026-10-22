using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.EntityFrameworkCore;

/// <summary>Selects preparation before change detection or validation before native saving.</summary>
public enum CapabilityPhase
{
    /// <summary>Runs selected preparation before the shared change-detection pass.</summary>
    Prepare = 1,

    /// <summary>Checks the detected native proposal before EF executes the save.</summary>
    Validate = 2,
}

/// <summary>Explicit model-selected native EF save composition; it does not own transactions or commit.</summary>
public static class CapabilityConfiguration
{
    private const string Prefix = "Gloaming:Requires:";

    /// <summary>Declares a required save capability in model metadata; the operation must register and install its matching integration.</summary>
    public static ModelBuilder RequireGloamingCapability(this ModelBuilder model, string key)
    {
        ArgumentNullException.ThrowIfNull(model);

        model.Model.RequireGloamingCapability(key);
        return model;
    }

    /// <summary>Declares a required save capability in model metadata; the operation must register and install its matching integration.</summary>
    public static void RequireGloamingCapability(this IMutableModel model, string key)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        model.SetAnnotation(Prefix + key, true);
    }

    /// <summary>Registers one context-typed preparation or validation callback; repeated identical registrations do not add another execution.</summary>
    public static IServiceCollection TryAddGloamingCapability<TContext>(
        this IServiceCollection services,
        string key,
        CapabilityPhase phase,
        Action<IServiceProvider, TContext> execute,
        Func<IServiceProvider, TContext, CancellationToken, Task>? executeAsync = null
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(execute);
        if (phase is not CapabilityPhase.Prepare and not CapabilityPhase.Validate)
            throw new ArgumentOutOfRangeException(nameof(phase));

        if (
            services.Any(service =>
                service.ImplementationInstance is Capability<TContext> existing
                && existing.Key == key
            )
        )
            return services;

        services.AddSingleton(
            new Capability<TContext>(
                key,
                phase,
                execute,
                executeAsync
                    ?? (
                        (provider, context, _) =>
                        {
                            execute(provider, context);
                            return Task.CompletedTask;
                        }
                    )
            )
        );
        return services;
    }

    /// <summary>Installs native save interception for the current scoped context using its model-selected capabilities.</summary>
    public static DbContextOptionsBuilder ConfigureGloamingCapabilities<TContext>(
        this DbContextOptionsBuilder options,
        IServiceProvider provider
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(provider);

        if (
            options
                .Options.FindExtension<CoreOptionsExtension>()
                ?.Interceptors?.Any(interceptor => interceptor is CapabilityInterceptor<TContext>)
            != true
        )
            options.AddInterceptors(new CapabilityInterceptor<TContext>(provider));

        return options;
    }

    private sealed record Capability<TContext>(
        string Key,
        CapabilityPhase Phase,
        Action<IServiceProvider, TContext> Execute,
        Func<IServiceProvider, TContext, CancellationToken, Task> ExecuteAsync
    )
        where TContext : DbContext;

    private sealed class CapabilityInterceptor<TContext>(IServiceProvider provider)
        : SaveChangesInterceptor
        where TContext : DbContext
    {
        private bool _executing;
        private bool _failed;

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result
        )
        {
            Enter();
            try
            {
                var context = (TContext)eventData.Context!;
                var plan = Select(context, result);
                bool detected = false;
                foreach (var capability in plan)
                {
                    if (capability.Phase == CapabilityPhase.Validate && !detected)
                    {
                        context.ChangeTracker.DetectChanges();
                        detected = true;
                    }

                    capability.Execute(provider, context);
                    CheckHealthy();
                }

                if (!detected && plan.Length > 0)
                    context.ChangeTracker.DetectChanges();

                return result;
            }
            catch
            {
                _failed = true;
                throw;
            }
            finally
            {
                _executing = false;
            }
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Enter();
            try
            {
                var context = (TContext)eventData.Context!;
                var plan = Select(context, result);
                bool detected = false;
                foreach (var capability in plan)
                {
                    if (capability.Phase == CapabilityPhase.Validate && !detected)
                    {
                        context.ChangeTracker.DetectChanges();
                        detected = true;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    await capability.ExecuteAsync(provider, context, cancellationToken);
                    CheckHealthy();
                }

                if (!detected && plan.Length > 0)
                    context.ChangeTracker.DetectChanges();

                return result;
            }
            catch
            {
                _failed = true;
                throw;
            }
            finally
            {
                _executing = false;
            }
        }

        private Capability<TContext>[] Select(TContext context, InterceptionResult<int> result)
        {
            var required = context
                .Model.GetAnnotations()
                .Where(annotation =>
                    annotation.Name.StartsWith(Prefix, StringComparison.Ordinal)
                    && annotation.Value is true
                )
                .Select(annotation => annotation.Name[Prefix.Length..])
                .ToHashSet(StringComparer.Ordinal);
            if (required.Count == 0)
                return [];

            if (result.HasResult)
                throw new InvalidOperationException(
                    "Suppressed saving cannot execute required Gloaming capabilities."
                );

            var available = provider.GetServices<Capability<TContext>>().ToArray();
            foreach (string key in required)
            {
                if (!available.Any(capability => capability.Key == key))
                    throw new InvalidOperationException(
                        $"Register the '{key}' capability for {typeof(TContext).Name}."
                    );
            }

            return available
                .Where(capability => required.Contains(capability.Key))
                .OrderBy(capability => capability.Phase)
                .ToArray();
        }

        private void Enter()
        {
            if (_executing || _failed)
            {
                _failed = true;
                throw new InvalidOperationException(
                    "Discard a failed capability operation; nested saving is unsupported."
                );
            }

            _executing = true;
        }

        private void CheckHealthy()
        {
            if (_failed)
                throw new InvalidOperationException(
                    "A nested save invalidated the capability operation."
                );
        }
    }
}
