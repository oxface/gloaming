using Gloaming.EventSourcing;

namespace Gloaming.EventSourcingTests;

public sealed class AggregateTests
{
    [Fact]
    public void CompleteCandidateIsValidatedOnceAndPendingFactsSpanDecisions()
    {
        var aggregate = new Counter(Guid.NewGuid(), 7, new State(4));
        Fact[] first = [new(23), new(-22)];
        aggregate.Change(first);
        first[0] = new(99);
        aggregate.Change([new(2)]);
        Assert.Equal(7, aggregate.ExpectedVersion);
        Assert.Equal(10, aggregate.Version);
        Assert.Equal(new State(7), aggregate.State);
        Assert.Equal([23, -22, 2], aggregate.PendingEvents.Select(fact => fact.Amount));
        Assert.Equal(2, aggregate.Validations);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Fact>)aggregate.PendingEvents).Add(new(1))
        );
    }

    [Theory]
    [InlineData("later-evolution")]
    [InlineData("candidate-policy")]
    [InlineData("null-event")]
    [InlineData("null-candidate")]
    [InlineData("version-overflow")]
    public void RejectedBatchPreservesPreviouslyAcceptedStateVersionAndPendingFacts(string fault)
    {
        var aggregate = new Counter(
            Guid.NewGuid(),
            fault == "version-overflow" ? long.MaxValue - 1 : 2,
            new State(4)
        );
        aggregate.Change([new(1)]);
        State? state = aggregate.State;
        long version = aggregate.Version;
        Fact[] accepted = aggregate.PendingEvents.ToArray();
        aggregate.NullCandidate = fault == "null-candidate";
        Fact[] rejected =
            fault == "later-evolution" ? [new(1), new(int.MinValue)]
            : fault == "candidate-policy" ? [new(1), new(30)]
            : fault == "null-event" ? [new(1), null!]
            : [new(1), new(2)];
        if (fault == "later-evolution")
            Assert.Throws<InvalidDataException>(() => aggregate.Change(rejected));
        else if (fault == "null-event")
            Assert.Throws<ArgumentNullException>(() => aggregate.Change(rejected));
        else if (fault == "version-overflow")
            Assert.Throws<OverflowException>(() => aggregate.Change(rejected));
        else
            Assert.Throws<InvalidOperationException>(() => aggregate.Change(rejected));
        Assert.Same(state, aggregate.State);
        Assert.Equal(version, aggregate.Version);
        Assert.Equal(accepted, aggregate.PendingEvents);
    }

    [Fact]
    public void HistoricalInitializationDoesNotApplyCurrentPolicyOrCreatePendingFacts()
    {
        var aggregate = new Counter(Guid.NewGuid(), 8, new State(90));
        Assert.Equal(0, aggregate.Validations);
        Assert.Equal(8, aggregate.ExpectedVersion);
        Assert.Equal(8, aggregate.Version);
        Assert.Empty(aggregate.PendingEvents);
        aggregate.Change([]);
        Assert.Equal(0, aggregate.Validations);
        Assert.Equal(new State(90), aggregate.State);
    }

    [Theory]
    [InlineData("empty-id")]
    [InlineData("negative-version")]
    [InlineData("state-without-history")]
    [InlineData("history-without-state")]
    public void InitializationRequiresConsistentObservedStateAndVersion(string fault)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new Counter(
                fault == "empty-id" ? Guid.Empty : Guid.NewGuid(),
                fault == "negative-version" ? -1
                    : fault == "state-without-history" ? 0
                    : 1,
                fault == "history-without-state" ? null : new State(1)
            )
        );
    }

    private sealed record Fact(int Amount);

    private sealed record State(int Value);

    private sealed class Counter(Guid id, long version, State? state)
        : EventSourcedAggregate<State, Fact>(id, version, state)
    {
        internal int Validations { get; private set; }
        internal bool NullCandidate { get; set; }

        internal void Change(IReadOnlyList<Fact> facts) => ApplyChanges(facts);

        protected override State Evolve(State? observed, IReadOnlyList<Fact> facts)
        {
            if (NullCandidate)
                return null!;
            int value = observed?.Value ?? 0;
            foreach (var fact in facts)
            {
                if (fact.Amount == int.MinValue)
                    throw new InvalidDataException("The later fact cannot evolve.");
                value = checked(value + fact.Amount);
            }
            return new(value);
        }

        protected override void ValidateCandidate(State candidate)
        {
            Validations++;
            if (candidate.Value > 25)
                throw new InvalidOperationException("Counter ceiling exceeded.");
        }
    }
}
