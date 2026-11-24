namespace Gloaming.Samples.Wholesale.RuntimeComposition.Tests;

public sealed class ContainerCleanupRuntimeTests
{
    private const string Machine = "local-machine";
    private static readonly Guid Boot = Guid.Parse("12345678-1234-1234-1234-123456789abc");
    private static readonly string Owner = $"{Machine}/{Boot:D}/101";

    [Fact]
    public void SelectsOnlyAbandonedWholesaleSessionsAndTheirTunnel()
    {
        string[] lines =
        [
            Row('a', Owner),
            Row('b', ""),
            Row('c', "", started: "unrelated-start"),
            Row('d', Owner, persistent: "true"),
            Row('e', $"another-machine/{Boot:D}/101", started: "foreign-start"),
        ];

        Assert.Equal(
            [new string('a', 64), new string('b', 64)],
            WholesaleContainerCleanup.FindAbandoned(lines, Machine, Boot, _ => false)
        );
    }

    [Theory]
    [InlineData(101)]
    [InlineData(102)]
    public void PreservesTheSessionWhileEitherOwnerIsAlive(int live)
    {
        string[] lines = [Row('a', Owner), Row('b', "")];

        Assert.Empty(
            WholesaleContainerCleanup.FindAbandoned(lines, Machine, Boot, pid => pid == live)
        );
    }

    private static string Row(
        char id,
        string owner,
        string started = "start",
        string persistent = "false"
    ) => $"{new string(id, 64)}\t{owner}\t102\t{started}\t{persistent}";
}
