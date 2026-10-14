using Gloaming.Events.History;

namespace Gloaming.EventHistoryTests;

public sealed class HistoryTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 3, 2, 3)]
    [InlineData(1, 1, 2, 1)]
    [InlineData(2, 1, 1, 2)]
    [InlineData(1, 0, 2, 0)]
    public void DamagedSequenceIsRejectedRatherThanSortedOrSkipped(
        long first,
        long second,
        long expected,
        long observed
    )
    {
        var failure = Assert.Throws<EventHistoryException>(() =>
            EventHistory.ValidateRange([new(first, StartedAt), new(second, StartedAt)], 0, 2)
        );
        Assert.Equal(EventHistoryFailure.UnexpectedVersion, failure.Failure);
        Assert.Equal(expected, failure.ExpectedVersion);
        Assert.Equal(observed, failure.ObservedVersion);
    }

    [Theory]
    [InlineData(3, 2, 2)]
    [InlineData(1, 2, 2)]
    [InlineData(2, 0, 0)]
    [InlineData(long.MaxValue, 2, 2)]
    public void MissingTailOrExcessRowsFailAgainstTheRequestedRange(
        long throughVersion,
        int count,
        long observed
    )
    {
        var positions = Enumerable
            .Range(1, count)
            .Select(version => new HistoryPosition(version, StartedAt));
        var failure = Assert.Throws<EventHistoryException>(() =>
            EventHistory.ValidateRange(positions, 0, throughVersion)
        );
        Assert.Equal(EventHistoryFailure.RangeMismatch, failure.Failure);
        Assert.Equal(throughVersion, failure.ExpectedVersion);
        Assert.Equal(observed, failure.ObservedVersion);
    }

    [Fact]
    public void ValidationSupportsANonzeroStartAndEqualRecordedTimes()
    {
        EventHistory.ValidateRange([new(43, StartedAt), new(44, StartedAt)], 42, 44);
    }

    [Fact]
    public void RegressionWithinTheReturnedRangeIdentifiesTheOffendingPosition()
    {
        var failure = Assert.Throws<EventHistoryException>(() =>
            EventHistory.ValidateRange(
                [new(43, StartedAt.AddSeconds(10)), new(44, StartedAt.AddSeconds(9))],
                42,
                44
            )
        );
        Assert.Equal(EventHistoryFailure.RecordedTimeRegression, failure.Failure);
        Assert.Null(failure.ExpectedVersion);
        Assert.Equal(44, failure.ObservedVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public void AnEmptyRangeRequiresNoInventedEventPosition(long boundary)
    {
        EventHistory.ValidateRange([], boundary, boundary);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(2, 1)]
    public void InvalidRangeBoundsAreCallerErrors(long afterVersion, long throughVersion)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EventHistory.ValidateRange([], afterVersion, throughVersion)
        );
    }

    [Fact]
    public void MaximumVersionDoesNotOverflowWhenAnExcessRowArrives()
    {
        EventHistory.ValidateRange(
            [new(long.MaxValue, StartedAt)],
            long.MaxValue - 1,
            long.MaxValue
        );
        var failure = Assert.Throws<EventHistoryException>(() =>
            EventHistory.ValidateRange(
                [new(long.MaxValue, StartedAt), new(long.MaxValue, StartedAt)],
                long.MaxValue - 1,
                long.MaxValue
            )
        );
        Assert.Equal(EventHistoryFailure.RangeMismatch, failure.Failure);
        Assert.Equal(long.MaxValue, failure.ExpectedVersion);
        Assert.Equal(long.MaxValue, failure.ObservedVersion);
    }

    [Fact]
    public void ASelectedRangeIsEnumeratedOnce()
    {
        int enumerations = 0;
        IEnumerable<HistoryPosition> Positions()
        {
            if (++enumerations > 1)
                throw new InvalidOperationException("The consumer range was enumerated again.");
            yield return new(1, StartedAt);
            yield return new(2, StartedAt.AddSeconds(10));
        }
        EventHistory.ValidateRange(Positions(), 0, 2);
        Assert.Equal(1, enumerations);
    }
}
