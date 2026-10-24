using System.Reflection;
using System.Text.Json;
using Gloaming.Tests.Infrastructure;
using Npgsql;
using CurrentApplication = Gloaming.Samples.WorkflowDeploymentDemo.Current.Application;
using EarlierApplication = Gloaming.Samples.WorkflowDeploymentDemo.Earlier.Application;

namespace Gloaming.Samples.WorkflowDeploymentDemo.Tests;

public sealed class DeploymentTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task CurrentBinaryContinuesOriginalInputStateAndDelayedReplyWithoutReplayingStart()
    {
        using var deadline = Deadline();
        var token = deadline.Token;
        var connection = await Setup(token);
        var key = Guid.NewGuid().ToString("D");
        var started = await Run(
            connection,
            earlier: true,
            "start",
            token,
            key,
            Fixture("input-v1")
        );
        Assert.Contains("BUILD earlier", started, StringComparison.Ordinal);
        Assert.Contains("METHOD start", started, StringComparison.Ordinal);
        var before = await Capture(connection, token);
        AssertOriginalFixtures(before);
        await Receive(connection, key, "reply-v1", token);
        var admitted = await Capture(connection, token);
        Assert.Equal(0, admitted.Processed);
        using var literalReply = JsonDocument.Parse(File.ReadAllText(Fixture("reply-v1")));
        var retainedReply = JsonSerializer
            .Deserialize<JsonElement>(admitted.InboxEnvelope)[0]
            .GetProperty("payload");
        Assert.True(JsonElement.DeepEquals(literalReply.RootElement, retainedReply));

        // The earlier app exited. Even an equal start in the replacement must not run new Start code.
        var repeated = await Run(
            connection,
            earlier: false,
            "start",
            token,
            key,
            Fixture("input-v1")
        );
        Assert.Contains("BUILD current", repeated, StringComparison.Ordinal);
        Assert.DoesNotContain("METHOD start", repeated, StringComparison.Ordinal);
        Assert.Equal(admitted, await Capture(connection, token));
        var continued = await Run(connection, earlier: false, "process", token);
        Assert.Contains("METHOD prepared", continued, StringComparison.Ordinal);
        Assert.DoesNotContain("METHOD start", continued, StringComparison.Ordinal);
        var paused = await Capture(connection, token);
        Assert.Equal(2, paused.Version);
        Assert.Equal("awaiting-approval", paused.State.GetProperty("phase").GetString());
        Assert.Equal("current", paused.State.GetProperty("reviewedByBuild").GetString());
        Assert.Equal(admitted.InitialInput, paused.InitialInput);
        Assert.Equal(admitted.Outbox, paused.Outbox);
        Assert.Equal(admitted.InboxEnvelope, paused.InboxEnvelope);
        Assert.Equal(1, paused.Processed);

        // The additive field is readable by the earlier binary too; rollback can lose this optional annotation.
        var rollbackRead = await Run(connection, earlier: true, "read", token, key);
        Assert.Contains("awaiting-approval", rollbackRead, StringComparison.Ordinal);
        await Run(connection, earlier: false, "drain", token, expectedExit: 12);
        await Run(connection, earlier: false, "signal", token, key);
        var completed = await Capture(connection, token);
        Assert.Equal(3, completed.Version);
        Assert.Equal("ready", completed.State.GetProperty("phase").GetString());
        await Run(connection, earlier: false, "drain", token);
        // Retained completions STILL require their definition; a drain does not migrate or erase them.
        await Run(
            connection,
            earlier: false,
            "read",
            token,
            key,
            mode: "incompatible",
            expectedExit: 10
        );
        Assert.Equal(completed, await Capture(connection, token));
    }

    [Theory]
    [InlineData("incompatible")]
    [InlineData("missing-binding")]
    [InlineData("wire-break")]
    public async Task BadDeploymentLeavesOriginalWorkRecoverableByCompatibleBinary(string mode)
    {
        using var deadline = Deadline();
        var token = deadline.Token;
        var connection = await Setup(token);
        var key = Guid.NewGuid().ToString("D");
        await Run(connection, earlier: true, "start", token, key, Fixture("input-v1"));
        await Receive(connection, key, "reply-v1", token);
        var original = await Capture(connection, token);
        // A same-version read cannot discover missing method bindings or changed wire contracts.
        await Run(
            connection,
            earlier: false,
            "read",
            token,
            key,
            mode: mode,
            expectedExit: mode == "incompatible" ? 10 : 0
        );
        var failure = await Run(
            connection,
            earlier: false,
            "process",
            token,
            mode: mode,
            expectedExit: 10
        );
        Assert.Contains("FAILED InvalidDataException", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("METHOD prepared", failure, StringComparison.Ordinal);
        var rejected = await Capture(connection, token);
        Assert.Equal(original.Process, rejected.Process);
        Assert.Equal(original.Outbox, rejected.Outbox);
        Assert.Equal(original.InboxEnvelope, rejected.InboxEnvelope);
        Assert.Equal(0, rejected.Processed);
        Assert.Equal(1, rejected.Failures);
        await Run(connection, earlier: false, "process", token);
        var recovered = await Capture(connection, token);
        Assert.Equal(2, recovered.Version);
        Assert.Equal(1, recovered.Processed);
        Assert.Equal(original.InitialInput, recovered.InitialInput);
        Assert.Equal(original.InboxEnvelope, recovered.InboxEnvelope);
    }

    [Fact]
    public async Task KilledReplacementRollsBackSavedTurnAndRecoversRetainedReplyInAnotherProcess()
    {
        using var deadline = Deadline();
        var token = deadline.Token;
        var connection = await Setup(token);
        var key = Guid.NewGuid().ToString("D");
        await Run(connection, earlier: true, "start", token, key, Fixture("input-v1"));
        await Receive(connection, key, "reply-v1", token);
        var original = await Capture(connection, token);
        var environment = EnvironmentFor(connection);
        environment["WORKFLOW_DEPLOYMENT_PAUSE_AFTER_SAVE"] = "true";
        await using (
            var killed = new ChildHost(typeof(CurrentApplication).Assembly, environment, "process")
        )
        {
            await killed.WaitForOutputAsync("TURN_SAVED_BEFORE_COMMIT", token);
            Assert.Equal(original, await Capture(connection, token));
            await killed.KillAsync(token);
        }

        Assert.Equal(original, await Capture(connection, token));
        await Run(connection, earlier: false, "process", token);
        var recovered = await Capture(connection, token);
        Assert.Equal(2, recovered.Version);
        Assert.Equal(1, recovered.Processed);
        Assert.Equal(0, recovered.Failures); // A killed attempt did not commit a failure observation.
        Assert.Equal(original.InboxEnvelope, recovered.InboxEnvelope);
        Assert.Equal(original.Outbox, recovered.Outbox);
    }

    [Fact]
    public async Task EarlierPausedInstanceResumesItsStoredSignalUnderCurrentCode()
    {
        using var deadline = Deadline();
        var token = deadline.Token;
        var connection = await Setup(token);
        var key = Guid.NewGuid().ToString("D");
        await Run(connection, earlier: true, "start", token, key, Fixture("input-v1"));
        await Run(connection, earlier: true, "drain", token, expectedExit: 12);
        await Receive(connection, key, "reply-v1", token);
        await Run(connection, earlier: true, "process", token);
        var earlierPause = await Capture(connection, token);
        Assert.Equal("awaiting-approval", earlierPause.State.GetProperty("phase").GetString());
        Assert.False(earlierPause.State.TryGetProperty("reviewedByBuild", out _));
        await Run(connection, earlier: false, "read", token, key);
        Assert.Equal(earlierPause, await Capture(connection, token));
        await Run(connection, earlier: false, "drain", token, expectedExit: 12);
        var resumed = await Run(connection, earlier: false, "signal", token, key);
        Assert.Contains("METHOD approve", resumed, StringComparison.Ordinal);
        Assert.DoesNotContain("METHOD start", resumed, StringComparison.Ordinal);
        Assert.DoesNotContain("METHOD prepared", resumed, StringComparison.Ordinal);
        var completed = await Capture(connection, token);
        Assert.Equal(3, completed.Version);
        Assert.Equal("ready", completed.State.GetProperty("phase").GetString());
        Assert.Equal(earlierPause.InitialInput, completed.InitialInput);
        Assert.Equal(earlierPause.Outbox, completed.Outbox);
    }

    [Fact]
    public async Task MissingRequiredHistoricalReplyFieldIsRejectedWithoutInventingAnOutcome()
    {
        using var deadline = Deadline();
        var token = deadline.Token;
        var connection = await Setup(token);
        var key = Guid.NewGuid().ToString("D");
        await Run(connection, earlier: true, "start", token, key, Fixture("input-v1"));
        await Receive(connection, key, "reply-missing-required-v1", token);
        var original = await Capture(connection, token);
        var failed = await Run(connection, earlier: false, "process", token, expectedExit: 10);
        Assert.Contains("FAILED JsonException", failed, StringComparison.Ordinal);
        var after = await Capture(connection, token);
        Assert.Equal(original.Process, after.Process);
        Assert.Equal(original.Outbox, after.Outbox);
        Assert.Equal(original.InboxEnvelope, after.InboxEnvelope);
        Assert.Equal(0, after.Processed);
        Assert.Equal(1, after.Failures);
        // Restoring compatible code cannot fix invalid original business data. It remains pending.
        await Run(connection, earlier: true, "process", token, expectedExit: 10);
        Assert.Equal(2, (await Capture(connection, token)).Failures);
    }

    private static CancellationTokenSource Deadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        return deadline;
    }

    private async Task<string> Setup(CancellationToken token)
    {
        var connection = await postgres.CreateDatabaseAsync(token);
        await Run(connection, earlier: true, "setup", token);
        return connection;
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".json");

    private static Dictionary<string, string> EnvironmentFor(string connection) =>
        new()
        {
            ["WORKFLOW_DEPLOYMENT_CONNECTION"] = connection,
            ["WORKFLOW_DEPLOYMENT_MODE"] = "compatible",
            ["WORKFLOW_DEPLOYMENT_PAUSE_AFTER_SAVE"] = "false",
        };

    private static async Task<string> Run(
        string connection,
        bool earlier,
        string action,
        CancellationToken token,
        string? key = null,
        string? input = null,
        string? delivery = null,
        string mode = "compatible",
        int expectedExit = 0
    )
    {
        Assembly assembly = earlier
            ? typeof(EarlierApplication).Assembly
            : typeof(CurrentApplication).Assembly;
        var environment = EnvironmentFor(connection);
        environment["WORKFLOW_DEPLOYMENT_MODE"] = mode;
        var arguments = new[] { action, key, input, delivery }.OfType<string>().ToArray();
        await using var child = new ChildHost(assembly, environment, arguments);
        var exit = await child.WaitForExitAsync(token);
        Assert.True(
            exit == expectedExit,
            $"Expected exit {expectedExit}; got {exit}. {child.Output}"
        );
        return child.Output;
    }

    private static async Task Receive(
        string connection,
        string key,
        string fixture,
        CancellationToken token
    ) =>
        _ = await Run(
            connection,
            earlier: true,
            "receive",
            token,
            key,
            Fixture(fixture),
            Guid.NewGuid().ToString("D")
        );

    private static void AssertOriginalFixtures(Observation observation)
    {
        using var input = JsonDocument.Parse(File.ReadAllText(Fixture("input-v1")));
        using var state = JsonDocument.Parse(File.ReadAllText(Fixture("state-v1")));
        Assert.True(
            JsonElement.DeepEquals(
                input.RootElement,
                JsonDocument.Parse(observation.InitialInput).RootElement
            )
        );
        Assert.True(JsonElement.DeepEquals(state.RootElement, observation.State));
        Assert.Equal(1, observation.Version);
    }

    private sealed record Observation(
        string Process,
        string Outbox,
        string InboxEnvelope,
        long Processed,
        long Failures
    )
    {
        private JsonElement Row => JsonSerializer.Deserialize<JsonElement>(Process);
        internal long Version => Row.GetProperty(nameof(Version)).GetInt64();
        internal string InitialInput => Row.GetProperty(nameof(InitialInput)).GetRawText();
        internal JsonElement State => Row.GetProperty(nameof(State));
    }

    private static async Task<Observation> Capture(string connectionString, CancellationToken token)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand(
            """
            SELECT
              (SELECT to_jsonb(p)::text FROM exports.processes p),
              (SELECT jsonb_agg(to_jsonb(o) ORDER BY message_id)::text FROM exports.outgoing o),
              (SELECT COALESCE(jsonb_agg(jsonb_build_object(
                'subscription', subscription_key, 'producer', producer_key, 'id', message_id,
                'receipt', receipt_id, 'alias', message_name, 'schema', schema_version,
                'payload', payload, 'tenant', tenant_key, 'correlation', correlation_id,
                'causation', causation_id, 'received', received_at
              ) ORDER BY message_id), '[]'::jsonb)::text FROM exports.incoming),
              (SELECT count(*) FROM exports.incoming WHERE processed_at IS NOT NULL),
              (SELECT COALESCE(sum(failure_count), 0)::bigint FROM exports.incoming)
            """,
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(token);
        Assert.True(await reader.ReadAsync(token));
        return new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetInt64(4)
        );
    }
}
