using Microsoft.EntityFrameworkCore;

namespace Gloaming.Auditing.EntityFrameworkCore;

/// <summary>Stages explicit audit entries in one owning context's native EF transaction.</summary>
public interface IAudit<TDbContext>
    where TDbContext : DbContext
{
    /// <summary>Tracks one envelope without I/O, save or commit. The caller owns final persistence.</summary>
    /// <remarks>Install ValidateAuditChanges in both native save overrides. Stage is not idempotent.</remarks>
    void Stage(AuditEntry entry);
}
