using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Auditing.EntityFrameworkCore;

namespace Gloaming.Auditing.Tests;

public sealed class EnvelopeTests
{
    [Fact]
    public void CapturesOwnedJsonExactOpaqueKeysAndUtcTimeWithoutInventingInitiator()
    {
        AuditEntry entry;
        using (var document = JsonDocument.Parse("{\"nested\":{\"version\":4}}"))
            entry = new AuditEntry(
                Guid.NewGuid(),
                new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.FromHours(3)),
                new ActorContext(Actor.System(new ActorId(" Worker "))),
                " Source ",
                " Action ",
                " Subject ",
                " key ",
                " Accepted ",
                1,
                document.RootElement,
                " Reason ",
                " Tenant "
            );

        Assert.Equal(4, entry.Details.GetProperty("nested").GetProperty("version").GetInt32());
        Assert.Equal(TimeSpan.Zero, entry.OccurredAt.Offset);
        Assert.Equal(7, entry.OccurredAt.Hour);
        Assert.Equal(" Worker ", entry.Attribution.Actor.Id!.Value);
        Assert.Null(entry.Attribution.Initiator);
        Assert.Equal(" Source ", entry.Source);
        Assert.Equal(" Action ", entry.Action);
        Assert.Equal(" Subject ", entry.SubjectType);
        Assert.Equal(" key ", entry.SubjectKey);
        Assert.Equal(" Accepted ", entry.Outcome);
        Assert.Equal(" Tenant ", entry.TenantKey);
        Assert.Equal(" Reason ", entry.ReasonCode);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("attribution")]
    [InlineData("source")]
    [InlineData("action")]
    [InlineData("subject-type")]
    [InlineData("subject-key")]
    [InlineData("outcome")]
    [InlineData("schema-zero")]
    [InlineData("schema-negative")]
    [InlineData("undefined")]
    [InlineData("null")]
    [InlineData("tenant")]
    [InlineData("reason")]
    public void InvalidEnvelopesFailBeforeStaging(string invalid)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new AuditEntry(
                invalid == "id" ? Guid.Empty : Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                invalid == "attribution" ? null! : new ActorContext(Actor.Anonymous),
                invalid == "source" ? " " : "module",
                invalid == "action" ? "" : "accepted",
                invalid == "subject-type" ? " " : "subject",
                invalid == "subject-key" ? " " : "key",
                invalid == "outcome" ? " " : "outcome",
                invalid == "schema-zero" ? 0
                    : invalid == "schema-negative" ? -1
                    : 1,
                invalid == "undefined"
                    ? default
                    : JsonSerializer.SerializeToElement(invalid == "null" ? null : new object()),
                reasonCode: invalid == "reason" ? "" : null,
                tenantKey: invalid == "tenant" ? " " : null
            )
        );
    }

    [Fact]
    public void AnonymousAndTenantlessAreExplicitAndIndependentOfOptionalInitiator()
    {
        var entry = AuditConsumer.Entry(
            attribution: new ActorContext(Actor.Anonymous, Actor.Human(new ActorId("initiator")))
        );
        Assert.Equal(ActorKind.Anonymous, entry.Attribution.Actor.Kind);
        Assert.Null(entry.Attribution.Actor.Id);
        Assert.Null(entry.TenantKey);
        Assert.Equal("initiator", entry.Attribution.Initiator!.Id!.Value);
    }
}
