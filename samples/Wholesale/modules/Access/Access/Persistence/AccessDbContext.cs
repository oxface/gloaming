using Gloaming.Auditing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Access.Persistence;

// Global registry queried before tenant establishment, not a tenant business-data context.
public sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options)
{
    internal DbSet<ExternalIdentityRow> ExternalIdentities => Set<ExternalIdentityRow>();
    internal DbSet<OrganizationRow> Organizations => Set<OrganizationRow>();
    internal DbSet<MembershipRow> Memberships => Set<MembershipRow>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateAuditChanges();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateAuditChanges();

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("access");
        modelBuilder
            .ConfigureAudit("access", "audit_entries")
            .Property(row => row.Details)
            .HasColumnType("jsonb");
        var user = modelBuilder.Entity<UserRow>();
        user.ToTable("users");
        user.HasKey(row => row.Id);
        user.Property(row => row.Id).HasColumnName("id").HasMaxLength(256).ValueGeneratedNever();

        var identity = modelBuilder.Entity<ExternalIdentityRow>();
        identity.ToTable("external_identities");
        identity.HasKey(row => new { row.Issuer, row.Subject });
        identity.Property(row => row.Issuer).HasColumnName("issuer").HasMaxLength(512);
        identity.Property(row => row.Subject).HasColumnName("subject").HasMaxLength(255);
        identity.Property(row => row.UserId).HasColumnName("user_id").HasMaxLength(256);
        identity
            .HasOne<UserRow>()
            .WithMany()
            .HasForeignKey(row => row.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        var organization = modelBuilder.Entity<OrganizationRow>();
        organization.ToTable(
            "organizations",
            table =>
                table.HasCheckConstraint(
                    "ck_organizations_slug",
                    "slug ~ '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$'"
                )
        );
        organization.HasKey(row => row.Id);
        organization
            .Property(row => row.Id)
            .HasColumnName("id")
            .HasMaxLength(256)
            .ValueGeneratedNever();
        organization.Property(row => row.Slug).HasColumnName("slug").HasMaxLength(63);
        organization.HasIndex(row => row.Slug).IsUnique();

        var membership = modelBuilder.Entity<MembershipRow>();
        membership.ToTable(
            "memberships",
            table =>
            {
                table.HasCheckConstraint("ck_memberships_status", "status IN (1, 2, 3)");
                table.HasCheckConstraint("ck_memberships_revision", "revision >= 1");
            }
        );
        membership.HasKey(row => row.Id);
        membership.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        membership
            .Property(row => row.OrganizationId)
            .HasColumnName("organization_id")
            .HasMaxLength(256)
            .IsConcurrencyToken();
        membership.Property(row => row.UserId).HasColumnName("user_id").HasMaxLength(256);
        membership.Property(row => row.Status).HasColumnName("status").HasConversion<int>();
        membership
            .Property(row => row.Revision)
            .HasColumnName("revision")
            .HasDefaultValue(1L)
            .IsConcurrencyToken();
        membership
            .Property(row => row.CanAdministerMemberships)
            .HasColumnName("can_administer_memberships")
            .HasDefaultValue(false);
        membership
            .HasIndex(row => new { row.OrganizationId, row.UserId })
            .IsUnique()
            .HasFilter("status IN (1, 2)");
        membership
            .HasOne<UserRow>()
            .WithMany()
            .HasForeignKey(row => row.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        membership
            .HasOne<OrganizationRow>()
            .WithMany()
            .HasForeignKey(row => row.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
