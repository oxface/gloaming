using System.Data.Common;
using Gloaming.ActorIdentity;
using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.Access.Contracts;
using Gloaming.Samples.Wholesale.Access.Persistence;
using Gloaming.Samples.Wholesale.Api;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Api;

public sealed partial class CompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentIdenticalOrOpposingIntentsCannotOverwriteAnAcceptedRevision(
        bool opposing
    )
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app);
        await using var connection = new NpgsqlConnection(
            app.Configuration.GetConnectionString("Access")
        );
        await connection.OpenAsync(Token);
        await using var barrier = await connection.BeginTransactionAsync(Token);
        await using (
            var command = new NpgsqlCommand(
                "SELECT id FROM access.organizations WHERE id = 'wholesale-alpha' FOR UPDATE",
                connection,
                barrier
            )
        )
            await command.ExecuteScalarAsync(Token);
        Task<MembershipChangeResult> first = ChangeMembershipAsync(app, true, 1);
        Task<MembershipChangeResult> second = ChangeMembershipAsync(app, !opposing, 1);
        try
        {
            await WaitForMembershipLocksAsync(app, 2);
        }
        finally
        {
            await barrier.RollbackAsync(CancellationToken.None);
        }

        var outcomes = await Task.WhenAll(first, second);
        Assert.Single(outcomes.OfType<MembershipChangeResult.Changed>());
        Assert.All(
            outcomes,
            outcome =>
                Assert.True(
                    outcome is MembershipChangeResult.Changed or MembershipChangeResult.Conflict
                        || (opposing && outcome is MembershipChangeResult.AlreadyInRequestedState)
                )
        );
        Assert.Equal(2, (await StoredMembershipAsync(app)).Status);
        Assert.Equal(2, (await StoredMembershipAsync(app)).Revision);
        Assert.Single(await MembershipAuditsAsync(app));
    }

    [Fact]
    public async Task ConcurrentAdministratorsCannotSuspendEachOtherAndLeaveNoActiveAdministrator()
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app, targetAdministrator: true);
        await using var connection = new NpgsqlConnection(
            app.Configuration.GetConnectionString("Access")
        );
        await connection.OpenAsync(Token);
        await using var barrier = await connection.BeginTransactionAsync(Token);
        await using (
            var command = new NpgsqlCommand(
                "SELECT id FROM access.organizations WHERE id = 'wholesale-alpha' FOR UPDATE",
                connection,
                barrier
            )
        )
            await command.ExecuteScalarAsync(Token);
        var first = ChangeMembershipAsync(app, true, 1);
        var second = ChangeMembershipAsync(
            app,
            true,
            1,
            AdministratorMembership,
            "application-alpha"
        );
        try
        {
            await WaitForMembershipLocksAsync(app, 2);
        }
        finally
        {
            await barrier.RollbackAsync(CancellationToken.None);
        }
        var outcomes = await Task.WhenAll(first, second);
        Assert.Single(outcomes.OfType<MembershipChangeResult.Changed>());
        Assert.Single(outcomes.OfType<MembershipChangeResult.NotFound>());
        var memberships = new[]
        {
            await StoredMembershipAsync(app),
            await StoredMembershipAsync(app, AdministratorMembership),
        };
        Assert.Single(memberships, row => row.Status == 1 && row.CanAdminister);
        Assert.Single(await MembershipAuditsAsync(app));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("membership")]
    [InlineData("commit")]
    public async Task FailureOfAnyTransactionalParticipantLeavesNeitherTransitionNorAcceptedAudit(
        string participant
    )
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app);
        await ExecuteAsync(
            app,
            participant switch
            {
                "audit" =>
                    "ALTER TABLE access.audit_entries ADD CONSTRAINT proof_membership_audit CHECK (schema_version > 1)",
                "membership" =>
                    "ALTER TABLE access.memberships ADD CONSTRAINT proof_membership_state CHECK (status <> 2)",
                _ => """
                CREATE FUNCTION access.proof_reject_membership_commit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'reject membership commit'; END; $$;
                CREATE CONSTRAINT TRIGGER proof_membership_commit AFTER UPDATE ON access.memberships
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION access.proof_reject_membership_commit();
                """,
            }
        );
        Assert.NotNull(await Record.ExceptionAsync(() => ChangeMembershipAsync(app, true, 1)));
        Assert.Equal(1, (await StoredMembershipAsync(app)).Status);
        Assert.Equal(1, (await StoredMembershipAsync(app)).Revision);
        Assert.Empty(await MembershipAuditsAsync(app));
        await ExecuteAsync(
            app,
            participant switch
            {
                "audit" =>
                    "ALTER TABLE access.audit_entries DROP CONSTRAINT proof_membership_audit",
                "membership" =>
                    "ALTER TABLE access.memberships DROP CONSTRAINT proof_membership_state",
                _ =>
                    "DROP TRIGGER proof_membership_commit ON access.memberships; DROP FUNCTION access.proof_reject_membership_commit()",
            }
        );
        Assert.IsType<MembershipChangeResult.Changed>(await ChangeMembershipAsync(app, true, 1));
        Assert.Single(await MembershipAuditsAsync(app));
    }

    [Fact]
    public async Task CommitRequiresTheExpectedMembershipAuditAndHistoricalRowsRemainIntact()
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app);
        await ExecuteAsync(
            app,
            """
            INSERT INTO access.memberships (id, organization_id, user_id, status)
            VALUES ('a1100000-0000-0000-0000-000000000099', 'wholesale-alpha', 'application-alpha', 3);
            CREATE FUNCTION access.proof_required_membership_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.status <> OLD.status AND NOT EXISTS (
                SELECT 1 FROM access.audit_entries
                WHERE tenant_key = NEW.organization_id AND subject_key = NEW.id::text
                  AND source = 'access' AND subject_type = 'membership' AND outcome = 'accepted'
                  AND action = CASE WHEN NEW.status = 2 THEN 'membership.suspended' ELSE 'membership.reactivated' END
                  AND actor_kind = 2 AND actor_key = 'membership-administrator' AND schema_version = 1
                  AND details->>'UserId' = NEW.user_id
                  AND (details->>'PreviousStatus')::integer = OLD.status
                  AND (details->>'Status')::integer = NEW.status
                  AND (details->>'PreviousRevision')::bigint = OLD.revision
                  AND (details->>'Revision')::bigint = NEW.revision
              ) THEN RAISE EXCEPTION 'membership committed without expected audit'; END IF;
              RETURN NULL;
            END; $$;
            CREATE CONSTRAINT TRIGGER proof_required_membership_audit AFTER UPDATE ON access.memberships
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION access.proof_required_membership_audit();
            """
        );
        var omitted = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(
                app,
                "UPDATE access.memberships SET status = 2, revision = revision + 1 WHERE id = 'a1100000-0000-0000-0000-000000000001'"
            )
        );
        Assert.Equal("membership committed without expected audit", omitted.MessageText);
        Assert.IsType<MembershipChangeResult.Changed>(await ChangeMembershipAsync(app, true, 1));
        Assert.IsType<MembershipChangeResult.Changed>(await ChangeMembershipAsync(app, false, 2));
        Assert.Equal(2, (await MembershipAuditsAsync(app)).Length);
        var history = await StoredMembershipAsync(
            app,
            Guid.Parse("a1100000-0000-0000-0000-000000000099")
        );
        Assert.Equal(3, history.Status);
        Assert.Equal(1, history.Revision);
        await using var scope = app.Services.CreateAsyncScope();
        var access = scope.ServiceProvider.GetRequiredService<IApplicationAccess>();
        Assert.Equal(
            new UserId("application-alpha"),
            await access.ResolveUserAsync(
                new ExternalIdentity("https://linked-identity.test", "shared-subject"),
                Token
            )
        );
    }

    [Fact]
    public async Task LostCommitResponseIsReconciledThroughFreshStateWithoutDuplicatingTheAudit()
    {
        var fault = new LostMembershipCommitResponse();
        await using var app = await StartAsync(configureHost: builder =>
            builder.Services.AddDbContext<AccessDbContext>(options =>
                options.AddInterceptors(fault)
            )
        );
        await PrepareMembershipsAsync(app);
        fault.Armed = true;
        await Assert.ThrowsAsync<IOException>(() => ChangeMembershipAsync(app, true, 1));
        await using (var scope = AdministrationScope(app))
        {
            var observed = await scope
                .ServiceProvider.GetRequiredService<IMembershipAdministration>()
                .ReadAsync(TargetMembership, Token);
            Assert.Equal(MembershipStatus.Suspended, observed!.Status);
            Assert.Equal(2, observed.Revision);
        }
        Assert.IsType<MembershipChangeResult.Conflict>(await ChangeMembershipAsync(app, true, 1));
        Assert.Single(await MembershipAuditsAsync(app));
    }

    [Fact]
    public async Task CancellationAfterSaveRollsBackMembershipAndAudit()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var interceptor = new CancelMembershipSave(cancellation);
        await using var app = await StartAsync(configureHost: builder =>
            builder.Services.AddDbContext<AccessDbContext>(options =>
                options.AddInterceptors(interceptor)
            )
        );
        await PrepareMembershipsAsync(app);
        await using (var scope = AdministrationScope(app))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                scope
                    .ServiceProvider.GetRequiredService<IMembershipAdministration>()
                    .SuspendAsync(new(TargetMembership, 1), cancellation.Token)
            );
        Assert.Equal(1, (await StoredMembershipAsync(app)).Revision);
        Assert.Empty(await MembershipAuditsAsync(app));
        Assert.IsType<MembershipChangeResult.Changed>(await ChangeMembershipAsync(app, true, 1));
    }

    [Fact]
    public async Task RemovedMembershipCannotBeReactivatedAndSuspendedCallerCannotAdministerDirectly()
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app);
        await ExecuteAsync(
            app,
            "UPDATE access.memberships SET status = 3 WHERE id = 'a1100000-0000-0000-0000-000000000001'"
        );
        Assert.IsType<MembershipChangeResult.InvalidTransition>(
            await ChangeMembershipAsync(app, false, 1)
        );
        await ExecuteAsync(
            app,
            "UPDATE access.memberships SET status = 2 WHERE user_id = 'membership-administrator'"
        );
        Assert.IsType<MembershipChangeResult.NotFound>(await ChangeMembershipAsync(app, false, 1));
        Assert.Empty(await MembershipAuditsAsync(app));
    }

    [Theory]
    [InlineData("missing-tenant")]
    [InlineData("tenantless")]
    [InlineData("missing-actor")]
    [InlineData("anonymous")]
    [InlineData("system")]
    public async Task DirectAdministrationRequiresExplicitSelectedTenantAndHumanActor(
        string missing
    )
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app);
        await using var scope = app.Services.CreateAsyncScope();
        if (missing != "missing-tenant")
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(
                    missing == "tenantless"
                        ? TenantContext.Tenantless()
                        : TenantContext.ForTenant(new TenantId("wholesale-alpha"))
                );
        if (missing != "missing-actor")
            scope
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(
                    new ActorContext(
                        missing switch
                        {
                            "anonymous" => Actor.Anonymous,
                            "system" => Actor.System(new ActorId("worker")),
                            _ => Actor.Human(new ActorId(Administrator)),
                        }
                    )
                );
        var result = await Record.ExceptionAsync(() =>
            scope
                .ServiceProvider.GetRequiredService<IMembershipAdministration>()
                .SuspendAsync(new(TargetMembership, 1), Token)
        );
        Assert.NotNull(result);
        Assert.IsAssignableFrom<InvalidOperationException>(result);
        Assert.Empty(await MembershipAuditsAsync(app));
    }

    [Fact]
    public async Task CompanyMandateAndContinuingHumanWorkChooseDifferentAdmissionAfterSuspension()
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app);
        Assert.IsType<MembershipChangeResult.Changed>(await ChangeMembershipAsync(app, true, 1));
        await using (var personal = app.Services.CreateAsyncScope())
        {
            var access = personal.ServiceProvider.GetRequiredService<IApplicationAccess>();
            var user = await access.ResolveUserAsync(
                new ExternalIdentity("https://identity.test", "shared-subject"),
                Token
            );
            personal
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(new ActorContext(Actor.Human(new ActorId(user!.Value))));
            Assert.Null(await access.ResolveMemberOrganizationAsync(user, "north-supply", Token));
            // The continuing-human job stops here, before publishing tenancy or reading business data.
            Assert.Throws<InvalidOperationException>(() =>
                personal.ServiceProvider.GetRequiredService<ITenantContextAccessor>().Current
            );
        }
        await using var company = app.Services.CreateAsyncScope();
        var attribution = new ActorContext(
            Actor.System(new ActorId("company-stock-reader")),
            Actor.Human(new ActorId("application-alpha"))
        );
        company
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(attribution);
        // This finite company mandate explicitly permits this system actor to read North's stock.
        company
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId("wholesale-alpha")));
        Assert.Equal(
            42,
            (
                await company
                    .ServiceProvider.GetRequiredService<IStockCatalog>()
                    .ReadAsync("DEMO-NOTEBOOK", Token)
            )!.AvailableQuantity
        );
        Assert.Equal(
            attribution,
            company.ServiceProvider.GetRequiredService<IActorContextAccessor>().Current
        );
    }

    [Fact]
    public async Task NativeMigrationPreservesPopulatedPreAdministrationAccessWithoutGrantingPrivileges()
    {
        string connectionString = await postgres.CreateDatabaseAsync(Token);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(Token);
            string fixture = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "Access-20a02be.sql"),
                Token
            );
            await using var command = new NpgsqlCommand(
                fixture
                    + """
                    INSERT INTO access.users (id) VALUES ('preserved-user');
                    INSERT INTO access.external_identities (issuer, subject, user_id) VALUES ('https://preserved.test', 'subject', 'preserved-user');
                    INSERT INTO access.organizations (id, slug) VALUES ('preserved-org', 'preserved');
                    INSERT INTO access.memberships (id, organization_id, user_id, status) VALUES
                        ('a1100000-0000-0000-0000-000000000001', 'preserved-org', 'preserved-user', 2);
                    """,
                connection
            );
            await command.ExecuteNonQueryAsync(Token);
        }
        var options = new DbContextOptionsBuilder<AccessDbContext>();
        AccessDatabase.Configure(options, connectionString);
        await using var database = new AccessDbContext(options.Options);
        await database.Database.MigrateAsync(Token);
        Assert.False(database.Database.HasPendingModelChanges());
        Assert.Equal(2, (await database.Database.GetAppliedMigrationsAsync(Token)).Count());
        var membership = await database
            .Database.SqlQuery<MembershipSnapshot>(
                $"""
                SELECT id AS "Id", user_id AS "UserId", status AS "Status", revision AS "Revision",
                    can_administer_memberships AS "CanAdminister" FROM access.memberships WHERE id = {TargetMembership}
                """
            )
            .SingleAsync(Token);
        Assert.Equal(
            new MembershipSnapshot(TargetMembership, "preserved-user", 2, 1, false),
            membership
        );
        Assert.Empty(await database.Set<AuditRecord>().ToArrayAsync(Token));
        Assert.Equal(
            "preserved-user",
            await database
                .Database.SqlQueryRaw<string>(
                    "SELECT user_id AS \"Value\" FROM access.external_identities WHERE issuer = 'https://preserved.test' AND subject = 'subject'"
                )
                .SingleAsync(Token)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncoordinatedTargetWriteStillLosesThroughNativeRevisionOrOrganizationPredicate(
        bool moveOrganization
    )
    {
        var writer = new OutsideMembershipRevision();
        await using var app = await StartAsync(configureHost: builder =>
            builder.Services.AddDbContext<AccessDbContext>(options =>
                options.AddInterceptors(writer)
            )
        );
        await PrepareMembershipsAsync(app);
        if (moveOrganization)
            await ExecuteAsync(
                app,
                "INSERT INTO access.organizations (id, slug) VALUES ('foreign-proof', 'foreign-proof')"
            );

        writer.ConnectionString = app.Configuration.GetConnectionString("Access")!;
        writer.MoveOrganization = moveOrganization;
        writer.Armed = true;
        Assert.IsType<MembershipChangeResult.Conflict>(await ChangeMembershipAsync(app, true, 1));
        Assert.False(writer.Armed);
        var persisted = await StoredMembershipAsync(app);
        Assert.Equal(1, persisted.Status);
        Assert.Equal(moveOrganization ? 1 : 2, persisted.Revision);
        Assert.Empty(await MembershipAuditsAsync(app));
        if (moveOrganization)
            Assert.IsType<MembershipChangeResult.NotFound>(
                await ChangeMembershipAsync(app, true, 1)
            );
        else
            Assert.IsType<MembershipChangeResult.Changed>(
                await ChangeMembershipAsync(app, true, 2)
            );
    }

    private sealed class OutsideMembershipRevision : DbCommandInterceptor
    {
        public string ConnectionString { get; set; } = "";
        public bool Armed { get; set; }
        public bool MoveOrganization { get; set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Armed
                && command.CommandText.Contains(
                    "UPDATE access.memberships",
                    StringComparison.Ordinal
                )
            )
            {
                Armed = false;
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using var competing = new NpgsqlCommand(
                    MoveOrganization
                        ? "UPDATE access.memberships SET organization_id = 'foreign-proof' WHERE id = 'a1100000-0000-0000-0000-000000000001'"
                        : "UPDATE access.memberships SET revision = revision + 1 WHERE id = 'a1100000-0000-0000-0000-000000000001'",
                    connection
                );
                await competing.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }

    private static async Task WaitForMembershipLocksAsync(WebApplication app, int count)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        await using var observer = new NpgsqlConnection(
            app.Configuration.GetConnectionString("Access")
        );
        await observer.OpenAsync(deadline.Token);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()
              AND pid <> pg_backend_pid() AND state = 'active' AND wait_event_type = 'Lock'
              AND query LIKE '%access.organizations%' AND query LIKE '%FOR UPDATE%'
            """,
            observer
        );
        while (
            Convert.ToInt64(
                await command.ExecuteScalarAsync(deadline.Token),
                System.Globalization.CultureInfo.InvariantCulture
            ) < count
        )
            await Task.Delay(25, deadline.Token);
    }

    private sealed class LostMembershipCommitResponse : DbTransactionInterceptor
    {
        public bool Armed { get; set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed)
            {
                Armed = false;
                throw new IOException("Membership commit response lost.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class CancelMembershipSave(CancellationTokenSource cancellation)
        : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (cancellationToken == cancellation.Token)
                cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }
}
