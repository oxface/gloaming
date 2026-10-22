using Gloaming.ActorIdentity;
using Gloaming.Tenancy;

using var actors = new ActorContextAccessor();
using var tenants = new TenantContextAccessor();
Expect<InvalidOperationException>(() => _ = actors.Current);
Expect<InvalidOperationException>(() => _ = tenants.Current);
var human = Actor.Human(new ActorId("person:7"));
var system = Actor.System(new ActorId("person:7"));
Check(human != system, "Actor kinds remain distinct for equal keys.");
actors.Initialize(new ActorContext(system, human));
tenants.Initialize(TenantContext.ForTenant(new TenantId(" Tenant:A ")));
Check(actors.Current.RequireIdentifiedActor() == system, "Executing actor retained.");
Check(actors.Current.Initiator == human, "Initiator retained independently.");
Check(tenants.Current.RequireTenant().Value == " Tenant:A ", "Opaque key preserved.");
Expect<InvalidOperationException>(() => actors.Initialize(new ActorContext(human)));
Expect<InvalidOperationException>(() => tenants.Initialize(TenantContext.Tenantless()));
Expect<IdentifiedActorRequiredException>(() =>
    new ActorContext(Actor.Anonymous).RequireIdentifiedActor()
);
Expect<TenantRequiredException>(() => TenantContext.Tenantless().RequireTenant());
Expect<ArgumentException>(() => new ActorId(" "));
Expect<ArgumentException>(() => new TenantId(" "));
Check(new TenantId("A") != new TenantId("a"), "Keys compare ordinally.");
Parallel.For(
    0,
    100,
    _ =>
        Check(
            actors.Current.Actor == system && tenants.Current.Tenant!.Value == " Tenant:A ",
            "Concurrent immutable reads."
        )
);
using var independent = new TenantContextAccessor();
independent.Initialize(TenantContext.ForTenant(new TenantId("other")));
Check(tenants.Current.Tenant!.Value == " Tenant:A ", "Operation scopes independent.");
actors.Dispose();
Expect<ObjectDisposedException>(() => _ = actors.Current);
Console.WriteLine(
    "PASS core: lifecycle, distinct identity, opaque keys, concurrent reads, independent scopes"
);

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Expect<T>(Action action)
    where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
