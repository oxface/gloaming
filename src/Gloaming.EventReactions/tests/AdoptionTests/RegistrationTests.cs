using Gloaming.EventReactions;
using Gloaming.EventReactions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.EventReactions.Consumer.Tests;

public sealed class RegistrationTests
{
    private sealed class FirstModule { }

    private sealed class SecondModule { }

    private sealed class FirstContext : DbContext { }

    private sealed class SecondContext : DbContext { }

    private sealed record RaisedFact;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdditionalRegistrationForOneContextIsRejectedBeforeChangingServices(bool sameTag)
    {
        var services = new ServiceCollection();
        services.AddScoped<List<string>>();
        services.AddEventReactions<FirstContext, FirstModule>(events =>
            events.AddReaction<RaisedFact, Recorder>(provider =>
                new("first", provider.GetRequiredService<List<string>>())
            )
        );
        var snapshot = services.ToArray();
        bool configured = false;

        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            if (sameTag)
                services.AddEventReactions<FirstContext, FirstModule>(_ => configured = true);
            else
                services.AddEventReactions<FirstContext, SecondModule>(_ => configured = true);
        });

        Assert.Contains(nameof(FirstContext), error.Message, StringComparison.Ordinal);
        Assert.False(configured);
        Assert.Equal(snapshot, services.ToArray());
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        using var operation = provider.CreateScope();
        operation
            .ServiceProvider.GetRequiredService<IEventSink<FirstModule>>()
            .Raise(new RaisedFact());
        operation
            .ServiceProvider.GetRequiredService<EventReactionDispatcher<FirstModule>>()
            .Dispatch(() => []);
        Assert.Equal(["first"], operation.ServiceProvider.GetRequiredService<List<string>>());
        Assert.Null(operation.ServiceProvider.GetService<IEventSink<SecondModule>>());
    }

    [Fact]
    public async Task RegisteredSinksAndHandlerInstancesAreModuleQualifiedAndOperationScoped()
    {
        var services = new ServiceCollection();
        services.AddScoped<List<string>>();
        int resolutions = 0;
        services.AddEventReactions<FirstContext, FirstModule>(events =>
            events.AddReaction<RaisedFact, Recorder>(provider =>
            {
                resolutions++;
                return new("first", provider.GetRequiredService<List<string>>());
            })
        );
        services.AddEventReactions<SecondContext, SecondModule>(events =>
            events.AddReaction<RaisedFact, Recorder>(provider =>
                new("second", provider.GetRequiredService<List<string>>())
            )
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        await using var operation = provider.CreateAsyncScope();
        var first = operation.ServiceProvider.GetRequiredService<
            EventReactionDispatcher<FirstModule>
        >();
        var second = operation.ServiceProvider.GetRequiredService<
            EventReactionDispatcher<SecondModule>
        >();
        operation
            .ServiceProvider.GetRequiredService<IEventSink<FirstModule>>()
            .Raise(new RaisedFact());
        Assert.Equal(0, resolutions);
        Assert.Equal(1, first.PendingCount);
        Assert.Equal(0, second.PendingCount);
        first.Dispatch(() => []);
        first.Dispatch(() => []);
        Assert.Equal(1, resolutions);
        Assert.Equal(["first"], operation.ServiceProvider.GetRequiredService<List<string>>());
        operation
            .ServiceProvider.GetRequiredService<IEventSink<SecondModule>>()
            .Raise(new RaisedFact());
        second.Dispatch(() => []);
        Assert.Equal(
            ["first", "second"],
            operation.ServiceProvider.GetRequiredService<List<string>>()
        );

        await using var fresh = provider.CreateAsyncScope();
        Assert.Equal(
            0,
            fresh
                .ServiceProvider.GetRequiredService<EventReactionDispatcher<FirstModule>>()
                .PendingCount
        );
        Assert.Empty(fresh.ServiceProvider.GetRequiredService<List<string>>());
    }

    private sealed class Recorder(string name, List<string> trace)
        : IEventReaction<RaisedFact>,
            ISynchronousEventReaction<RaisedFact>
    {
        public void Handle(RaisedFact fact) => trace.Add(name);

        public Task HandleAsync(RaisedFact fact, CancellationToken cancellationToken)
        {
            Handle(fact);
            return Task.CompletedTask;
        }
    }
}
