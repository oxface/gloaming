using Gloaming.ActorIdentity;

namespace Gloaming.ActorIdentityTests;

public sealed class IdentityAndContextTests
{
    [Fact]
    public void NullKeysAreRejected() =>
        Assert.Throws<ArgumentNullException>(() => new ActorId(null!));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void BlankKeysAreRejected(string key) =>
        Assert.Throws<ArgumentException>(() => new ActorId(key));

    [Theory]
    [InlineData("ACME", "acme")]
    [InlineData("é", "e\u0301")]
    [InlineData(" 42", "42")]
    [InlineData("42 ", "42")]
    [InlineData("00042", "42")]
    public void KeysPreserveExactValuesAndCompareOrdinally(string first, string second)
    {
        var actor = new ActorId(first);
        Assert.Equal(first, actor.Value);
        Assert.Equal(actor, new ActorId(first));
        Assert.NotEqual(actor, new ActorId(second));
    }

    [Fact]
    public void IdentifiedActorsRequireKeysAndActorKindParticipatesInEquality()
    {
        Assert.Throws<ArgumentNullException>(() => Actor.Human(null!));
        Assert.Throws<ArgumentNullException>(() => Actor.System(null!));
        Actor human = Actor.Human(new ActorId("42"));
        Actor system = Actor.System(new ActorId("42"));
        Assert.Equal(human, Actor.Human(new ActorId("42")));
        Assert.Equal(system, Actor.System(new ActorId("42")));
        Assert.NotEqual(human, system);
        Assert.NotEqual(human, Actor.Anonymous);
        Assert.Equal(ActorKind.Anonymous, Actor.Anonymous.Kind);
        Assert.Null(Actor.Anonymous.Id);
    }

    [Fact]
    public void ContextRejectsMissingActor() =>
        Assert.Throws<ArgumentNullException>(() => new ActorContext(null!));

    [Fact]
    public void InitiatorIsExplicitOptionalAttributionAndDoesNotIdentifyAnonymousActor()
    {
        Actor human = Actor.Human(new ActorId("user-42"));
        Actor workflow = Actor.System(new ActorId("workflow"));
        var deferred = new ActorContext(workflow, human);
        var fresh = new ActorContext(workflow);
        var anonymous = new ActorContext(Actor.Anonymous, human);
        var anonymousOrigin = new ActorContext(workflow, Actor.Anonymous);
        Assert.Same(workflow, deferred.RequireIdentifiedActor());
        Assert.Same(human, deferred.Initiator);
        Assert.Null(fresh.Initiator);
        Assert.NotEqual(deferred, fresh);
        Assert.Same(Actor.Anonymous, anonymousOrigin.Initiator);
        Assert.Throws<IdentifiedActorRequiredException>(() => anonymous.RequireIdentifiedActor());
    }

    [Fact]
    public void RequirementCheckRejectsMissingContext()
    {
        ActorContext absent = null!;
        Assert.Throws<ArgumentNullException>(() => absent.RequireIdentifiedActor());
    }

    [Fact]
    public void ActorKindsReserveZeroAndKeepExplicitValues()
    {
        Assert.Equal(1, (int)ActorKind.Anonymous);
        Assert.Equal(2, (int)ActorKind.Human);
        Assert.Equal(3, (int)ActorKind.System);
        Assert.False(Enum.IsDefined((ActorKind)0));
    }
}
