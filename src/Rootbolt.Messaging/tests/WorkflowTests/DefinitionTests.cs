using System.Text.Json;
using Rootbolt.Messaging.Workflows;

namespace Rootbolt.Messaging.WorkflowTests;

public sealed class DefinitionTests
{
    public sealed record Input(string Name);

    public sealed record State(string Value);

    public sealed record Reply(bool Success);

    [Fact]
    public void StartIsRequiredAndMayOnlyBeBoundOnce()
    {
        var builder = Builder();
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        builder.StartWith((context, input) => WorkflowTurn<State>.Complete(new(input.Name)));
        Assert.Throws<InvalidOperationException>(() =>
            builder.StartWith((context, input) => WorkflowTurn<State>.Complete(new(input.Name)))
        );
        Assert.Equal("setup", builder.Build().WorkflowType);
    }

    [Fact]
    public void ReplyAndSignalNamesShareOneNamespace()
    {
        var builder = Builder()
            .OnReply<Reply>(
                "ready",
                "resource.ready",
                1,
                (context, state, reply) => WorkflowTurn<State>.Complete(state)
            );
        Assert.Throws<InvalidOperationException>(() =>
            builder.OnSignal<Input>(
                "ready",
                (context, state, input) => WorkflowTurn<State>.Complete(state)
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            builder.OnReply<Reply>(
                "ready",
                "other",
                2,
                (context, state, reply) => WorkflowTurn<State>.Complete(state)
            )
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DefinitionAndWireVersionsMustBePositive(int version)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowBuilder<Input, State>("setup", version, new())
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Builder()
                .OnReply<Reply>(
                    "ready",
                    "ready",
                    version,
                    (context, state, reply) => WorkflowTurn<State>.Complete(state)
                )
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ContinuationNamesMustBePresent(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            Builder()
                .OnSignal<Input>(
                    name,
                    (context, state, input) => WorkflowTurn<State>.Complete(state)
                )
        );
        Assert.Throws<ArgumentException>(() =>
            WorkflowTurn<State>.WaitForInput(new("value"), name)
        );
    }

    [Fact]
    public void ReturnInstructionsCannotContainNullStateOrCommand()
    {
        Assert.Throws<ArgumentNullException>(() => WorkflowTurn<State>.Complete(null!));
        Assert.Throws<ArgumentNullException>(() =>
            WorkflowTurn<State>.Request(new("value"), null!, "ready")
        );
    }

    [Fact]
    public void InvalidZeroIsNotAnExecutionStatus() =>
        Assert.False(Enum.IsDefined((WorkflowStatus)0));

    private static WorkflowBuilder<Input, State> Builder() =>
        new("setup", 1, new(JsonSerializerDefaults.Web));
}
