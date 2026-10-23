# 0004: Separate module business Contracts from native host composition

Status: accepted and implemented. The sample composes Access, Sales and Inventory.

Access, Sales and Inventory own separate implementation/Contracts projects, internal business rows
and module-local native persistence. Business endpoints and HTTP identity/admission bridges
call Contracts; the host's composition, native migration tooling and finite setup may use
public DbContexts/configuration to keep registration, saves and transactions explicit.
This limited composition exception avoids a custom persistence facade while preserving
reviewable business-call boundaries. Module implementations do not reference each other or
the host; Inventory stores the admitted Organization key without an Access database FK.

The boundary is consumer-owned sample policy. It does not change independently adoptable
technical libraries or prevent privileged native EF/raw SQL access. See
[executable module ownership](../../samples/Wholesale/modules/README.md).
