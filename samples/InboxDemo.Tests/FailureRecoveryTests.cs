using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.InboxDemo.Tests;

[Collection("Rendering PostgreSQL")]
public sealed class FailureRecoveryTests(PostgreSqlFixture postgres)
{
    private const string OperatorToken = "test-operator-credential-with-at-least-32-characters";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AuthorizedRetryPreservesEnvelopeAcrossRepeatedFailureAndOtherWorkContinues()
    {
        string connection = await DatabaseAsync();
        await AdoptionTests.SqlAsync(
            connection,
            "ALTER TABLE rendering.jobs ADD CONSTRAINT unavailable_renderer CHECK (false) NOT VALID"
        );
        await using var app = InboxDemoHost.Build(
            connection,
            "http://127.0.0.1:0",
            operatorToken: OperatorToken
        );
        await app.StartAsync(Token);
        using var client = Client(app.Services);
        Guid message = Guid.NewGuid(),
            export = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Accepted, await SubmitAsync(client, message, export, 3));
        await Assert.ThrowsAsync<DbUpdateException>(() => AdoptionTests.ProcessAsync(app.Services));
        var stopped = await ReadAsync(connection, message);
        Assert.NotNull(stopped.QuarantinedAt);
        Assert.Equal("render-review-required", stopped.LastFailureCode);
        Assert.Equal(1, stopped.FailureCount);
        Assert.Equal(InboxProcessingResult.NoWork, await AdoptionTests.ProcessAsync(app.Services));
        Assert.Equal(HttpStatusCode.Accepted, await SubmitAsync(client, message, export, 3));
        Assert.Equal(HttpStatusCode.Conflict, await SubmitAsync(client, message, export, 4));
        Assert.Equal(stopped.Revision, (await ReadAsync(connection, message)).Revision);

        using (
            var noAuth = await client.PostAsJsonAsync(
                $"/operations/inbox/{message}/retry",
                new { stopped.Revision },
                Token
            )
        )
            Assert.Equal(HttpStatusCode.Unauthorized, noAuth.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "wrong-credential"
        );
        using (
            var wrong = await client.PostAsJsonAsync(
                $"/operations/inbox/{message}/retry",
                new { expectedRevision = stopped.Revision },
                Token
            )
        )
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        Assert.Equal(stopped.Revision, (await ReadAsync(connection, message)).Revision);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            OperatorToken
        );
        using (var inspection = await client.GetAsync($"/operations/inbox/{message}", Token))
        {
            Assert.Equal(HttpStatusCode.OK, inspection.StatusCode);
            string body = await inspection.Content.ReadAsStringAsync(Token);
            Assert.DoesNotContain("payload", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(OperatorToken, body, StringComparison.Ordinal);
        }

        Assert.Equal(HttpStatusCode.NoContent, await RetryAsync(client, message, stopped.Revision));
        await Assert.ThrowsAsync<DbUpdateException>(() => AdoptionTests.ProcessAsync(app.Services));
        var again = await ReadAsync(connection, message);
        Assert.Equal(2, again.FailureCount);
        Assert.Equal(1, again.FailuresSinceRedrive);
        Assert.NotNull(again.QuarantinedAt);
        Assert.Equal(HttpStatusCode.Conflict, await RetryAsync(client, message, stopped.Revision));
        Assert.Equal(again.Revision, (await ReadAsync(connection, message)).Revision);

        await AdoptionTests.SqlAsync(
            connection,
            "ALTER TABLE rendering.jobs DROP CONSTRAINT unavailable_renderer"
        );
        Guid another = Guid.NewGuid();
        Assert.Equal(
            HttpStatusCode.Accepted,
            await SubmitAsync(client, another, Guid.NewGuid(), 2)
        );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await AdoptionTests.ProcessAsync(app.Services)
        );
        Assert.NotNull((await ReadAsync(connection, another)).ProcessedAt);
        Assert.Equal(HttpStatusCode.NoContent, await RetryAsync(client, message, again.Revision));
        Assert.Equal(
            InboxProcessingResult.Processed,
            await AdoptionTests.ProcessAsync(app.Services)
        );
        var completed = await ReadAsync(connection, message);
        Assert.Equal(2, completed.FailureCount);
        Assert.Equal(0, completed.FailuresSinceRedrive);
        Assert.Equal(2, completed.RedriveCount);
        Assert.Equal(5, completed.Revision);
        Assert.NotNull(completed.ProcessedAt);
        Assert.Equal(stopped.ReceivedAt, completed.ReceivedAt);
        Assert.True(JsonElement.DeepEquals(stopped.Payload, completed.Payload));
        Assert.Equal(stopped.CorrelationId, completed.CorrelationId);
        Assert.Equal(stopped.CausationId, completed.CausationId);
        Assert.Equal(
            HttpStatusCode.Conflict,
            await RetryAsync(client, message, completed.Revision)
        );
        Assert.Equal(HttpStatusCode.Accepted, await SubmitAsync(client, message, export, 3));
        Assert.Equal(InboxProcessingResult.NoWork, await AdoptionTests.ProcessAsync(app.Services));
        await using var read = RenderDbContext.Create(connection);
        Assert.Equal(2, await read.Set<RenderJob>().CountAsync(Token));
        Assert.Null(read.Model.FindEntityType(typeof(OutboxMessageRecord)));
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task RetryReexecutesConsumerAdmissionAndCannotForceAnUnsupportedMessage()
    {
        string connection = await DatabaseAsync();
        await using var app = InboxDemoHost.Build(
            connection,
            "http://127.0.0.1:0",
            operatorToken: OperatorToken
        );
        await app.StartAsync(Token);
        var message = new IncomingMessage(
            Guid.NewGuid(),
            "exports",
            "unsupported",
            9,
            JsonSerializer.SerializeToElement(new { ExportRequestId = Guid.NewGuid(), Pages = 2 })
        );
        // Privileged intake deliberately bypasses the endpoint's admission to exercise the handler check.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<RenderDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await scope
                .ServiceProvider.GetRequiredService<IInbox<RenderDbContext>>()
                .ReceiveAsync(RenderExportHandler.Subscription, message, Token);
            await transaction.CommitAsync(Token);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            AdoptionTests.ProcessAsync(app.Services)
        );
        var stopped = await ReadAsync(connection, message.MessageId);
        using var client = Client(app.Services);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            OperatorToken
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            await RetryAsync(client, message.MessageId, stopped.Revision)
        );
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            AdoptionTests.ProcessAsync(app.Services)
        );
        var again = await ReadAsync(connection, message.MessageId);
        Assert.Equal("render-command-conflict", again.LastFailureCode);
        Assert.Equal(2, again.FailureCount);
        Assert.NotNull(again.QuarantinedAt);
        Assert.Equal(9, again.SchemaVersion);
        await using var read = RenderDbContext.Create(connection);
        Assert.Empty(await read.Set<RenderJob>().ToArrayAsync(Token));
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task OperatorRoutesAreAbsentWithoutConfigurationAndDenyBeforeStorageWithIt()
    {
        const string unavailable =
            "Host=127.0.0.1;Port=1;Database=unavailable;Username=not-used;Timeout=1";
        Assert.Throws<ArgumentException>(() =>
            InboxDemoHost.Build(unavailable, "http://127.0.0.1:0", operatorToken: "")
        );
        await using (var disabled = InboxDemoHost.Build(unavailable, "http://127.0.0.1:0"))
        {
            await disabled.StartAsync(Token);
            using var client = Client(disabled.Services);
            using var absent = await client.GetAsync($"/operations/inbox/{Guid.NewGuid()}", Token);
            Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
            await disabled.StopAsync(Token);
        }

        await using var enabled = InboxDemoHost.Build(
            unavailable,
            "http://127.0.0.1:0",
            operatorToken: OperatorToken
        );
        await enabled.StartAsync(Token);
        using var denied = Client(enabled.Services);
        using var get = await denied.GetAsync($"/operations/inbox/{Guid.NewGuid()}", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await RetryAsync(denied, Guid.NewGuid(), 0));
        await enabled.StopAsync(Token);
    }

    private async Task<string> DatabaseAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var database = RenderDbContext.Create(connection);
        await database.Database.MigrateAsync(Token);
        return connection;
    }

    private static HttpClient Client(IServiceProvider services) =>
        new()
        {
            BaseAddress = new Uri(AdoptionTests.Address(services), "/"),
            Timeout = TimeSpan.FromSeconds(10),
        };

    private static async Task<InboxMessageRecord> ReadAsync(string connection, Guid id)
    {
        await using var database = RenderDbContext.Create(connection);
        return await database
            .Set<InboxMessageRecord>()
            .SingleAsync(row => row.MessageId == id, Token);
    }

    private static async Task<HttpStatusCode> RetryAsync(
        HttpClient client,
        Guid message,
        long revision
    )
    {
        using var response = await client.PostAsJsonAsync(
            $"/operations/inbox/{message}/retry",
            new { expectedRevision = revision },
            Token
        );
        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> SubmitAsync(
        HttpClient client,
        Guid message,
        Guid export,
        int pages
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/commands")
        {
            Content = JsonContent.Create(
                new { ExportRequestId = export, Pages = pages },
                options: new JsonSerializerOptions()
            ),
        };
        request.Headers.Add("X-Message-Id", message.ToString());
        request.Headers.Add("X-Message-Name", "exports.render");
        request.Headers.Add("X-Message-Schema", "1");
        request.Headers.Add("X-Correlation-Id", "render-conversation");
        request.Headers.Add("X-Causation-Id", "render-parent");
        using var response = await client.SendAsync(request, Token);
        return response.StatusCode;
    }
}
