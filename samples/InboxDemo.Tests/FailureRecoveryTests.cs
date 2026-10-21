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
    public async Task AuthorizedOperatorDiscoversPagesInspectsAndRetriesOnlyTheConfiguredLane()
    {
        string connection = await DatabaseAsync();
        await AdoptionTests.SqlAsync(
            connection,
            "ALTER TABLE rendering.jobs ADD CONSTRAINT renderer_unavailable CHECK (false) NOT VALID"
        );
        await using var app = InboxDemoHost.Build(
            connection,
            "http://127.0.0.1:0",
            operatorToken: OperatorToken
        );
        await app.StartAsync(Token);
        using var client = Client(app.Services);
        Guid[] messages = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        foreach (Guid message in messages)
        {
            Assert.Equal(
                HttpStatusCode.Accepted,
                await SubmitAsync(client, message, Guid.NewGuid(), 2)
            );
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                AdoptionTests.ProcessAsync(app.Services)
            );
        }

        // Privileged fixture intake exercises the handler's producer admission separately.
        Guid foreign = Guid.NewGuid();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<RenderDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await scope
                .ServiceProvider.GetRequiredService<IInbox<RenderDbContext>>()
                .ReceiveAsync(
                    RenderExportHandler.Subscription,
                    new(
                        foreign,
                        "other",
                        "exports.render",
                        1,
                        JsonSerializer.SerializeToElement(
                            new { ExportRequestId = Guid.NewGuid(), Pages = 2 }
                        )
                    ),
                    Token
                );
            await transaction.CommitAsync(Token);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            AdoptionTests.ProcessAsync(app.Services)
        );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            OperatorToken
        );
        const string route = "/operations/inbox/dead-letters";
        var all = new List<InboxDeadLetter>();
        string? cursor = null;
        do
        {
            string url = route + "?pageSize=1&failureCode=render-review-required&producerKey=other";
            if (cursor is not null)
                url += "&after=" + Uri.EscapeDataString(cursor);

            using var response = await client.GetAsync(url, Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string json = await response.Content.ReadAsStringAsync(Token);
            Assert.DoesNotContain("payload", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tenantKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("exception", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("quarantinedAt", json, StringComparison.OrdinalIgnoreCase);
            var page = (await response.Content.ReadFromJsonAsync<InboxDeadLetterPage>(Token))!;
            Assert.Single(page.Items);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.Equal(3, all.Count);
        Assert.Equal(messages.Order(), all.Select(item => item.MessageId).Order());
        Assert.All(all, item => Assert.Equal("exports", item.ProducerKey));
        using (var hidden = await client.GetAsync($"{route}/{foreign}", Token))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        var first = all[0];
        var inspected = (
            await client.GetFromJsonAsync<InboxDeadLetter>($"{route}/{first.MessageId}", Token)
        )!;
        Assert.Equal(first, inspected);
        Assert.Equal(
            HttpStatusCode.NoContent,
            await RetryAsync(client, first.MessageId, inspected.Revision)
        );
        using (var gone = await client.GetAsync($"{route}/{first.MessageId}", Token))
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

        await Assert.ThrowsAsync<DbUpdateException>(() => AdoptionTests.ProcessAsync(app.Services));
        Assert.Equal(
            HttpStatusCode.Conflict,
            await RetryAsync(client, first.MessageId, inspected.Revision)
        );
        Assert.Equal(
            2,
            (
                await client.GetFromJsonAsync<InboxDeadLetter>($"{route}/{first.MessageId}", Token)
            )!.FailureCount
        );
        using (var missing = await client.GetAsync($"{route}/{Guid.NewGuid()}", Token))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await app.StopAsync(Token);
    }

    [Fact]
    public async Task OperatorRejectsBadPagingBeforeOpeningUnavailableStorage()
    {
        const string unavailable =
            "Host=127.0.0.1;Port=1;Database=absent;Username=unused;Timeout=1";
        await using var app = InboxDemoHost.Build(
            unavailable,
            "http://127.0.0.1:0",
            operatorToken: OperatorToken
        );
        await app.StartAsync(Token);
        using var client = Client(app.Services);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            OperatorToken
        );
        foreach (
            string query in new[]
            {
                "?pageSize=0",
                "?pageSize=101",
                "?pageSize=bad",
                "?failureCode=",
                "?failureCode=%20",
                "?after=bad!",
                "?after=",
            }
        )
        {
            using var response = await client.GetAsync(
                "/operations/inbox/dead-letters" + query,
                Token
            );
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (
            var empty = await client.GetAsync($"/operations/inbox/dead-letters/{Guid.Empty}", Token)
        )
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        await app.StopAsync(Token);
    }

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
        Assert.NotNull(stopped.DeadLetteredAt);
        Assert.Equal("render-review-required", stopped.LastFailureCode);
        Assert.Equal(1, stopped.FailureCount);
        Assert.Equal(InboxProcessingResult.NoWork, await AdoptionTests.ProcessAsync(app.Services));
        Assert.Equal(HttpStatusCode.Accepted, await SubmitAsync(client, message, export, 3));
        Assert.Equal(HttpStatusCode.Conflict, await SubmitAsync(client, message, export, 4));
        Assert.Equal(stopped.Revision, (await ReadAsync(connection, message)).Revision);

        using (
            var noAuth = await client.PostAsJsonAsync(
                $"/operations/inbox/dead-letters/{message}/retry",
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
                $"/operations/inbox/dead-letters/{message}/retry",
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
        using (
            var inspection = await client.GetAsync(
                $"/operations/inbox/dead-letters/{message}",
                Token
            )
        )
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
        Assert.NotNull(again.DeadLetteredAt);
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
        Assert.NotNull(again.DeadLetteredAt);
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
            using var absent = await client.GetAsync(
                $"/operations/inbox/dead-letters/{Guid.NewGuid()}",
                Token
            );
            Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
            using var list = await client.GetAsync("/operations/inbox/dead-letters", Token);
            Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
            await disabled.StopAsync(Token);
        }

        await using var enabled = InboxDemoHost.Build(
            unavailable,
            "http://127.0.0.1:0",
            operatorToken: OperatorToken
        );
        await enabled.StartAsync(Token);
        using var denied = Client(enabled.Services);
        using var get = await denied.GetAsync(
            $"/operations/inbox/dead-letters/{Guid.NewGuid()}",
            Token
        );
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        using var deniedList = await denied.GetAsync(
            "/operations/inbox/dead-letters?pageSize=0&after=bad!",
            Token
        );
        Assert.Equal(HttpStatusCode.Unauthorized, deniedList.StatusCode);
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
            $"/operations/inbox/dead-letters/{message}/retry",
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
