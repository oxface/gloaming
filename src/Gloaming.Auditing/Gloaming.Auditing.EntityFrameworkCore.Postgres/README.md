# Gloaming.Auditing.EntityFrameworkCore.Postgres

Optional PostgreSQL mapping for explicit transactional audit. It references
`Gloaming.Auditing.EntityFrameworkCore` and `Npgsql.EntityFrameworkCore.PostgreSQL`.
The EF package remains usable without this provider dependency.

```csharp
using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.Auditing.EntityFrameworkCore.Postgres;

var audit = modelBuilder.ConfigurePostgresAudit("operations", "accepted_log");
audit.Property(row => row.Id).HasColumnName("audit_id");
```

`ConfigurePostgresAudit(ModelBuilder, string schema, string table)` returns the editable
native `EntityTypeBuilder<AuditRecord>`. It calls the relational mapping and specializes
Details as JSONB, including the default relational subject timeline index. It installs
no clock defaults, ownership or retention policy.
Use the EF package's explicit staging and both save guards; schema creation, migrations,
native transaction, saves and commit remain consumer-owned.

The relational mapping includes ix_audit_subject_timeline on tenant/type/item/time/ID.
Consumers can rename or replace it through native EF configuration. Global/system entries can
omit SubjectKey; tenant ownership is an independent choice. JSONB storage normalizes JSON;
the pre-save envelope guard compares exact captured JSON text before database storage.

See [EF setup and guarantees](../Gloaming.Auditing.EntityFrameworkCore/README.md),
[transactional contract](../docs/transactional-audit.md) and
[current proof results](../../../docs/reports/e9-explicit-transactional-audit.md).
