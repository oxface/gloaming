# 0004: Separate module business Contracts from native host composition

Status: accepted and implemented. The current sample composes Access, Sales and Inventory;
the original boundary was owner-approved with E3.4.

Access, Sales and Inventory own separate implementation/Contracts projects, internal business rows
and module-local native persistence. Business endpoints and HTTP identity/admission bridges
call Contracts; the host's composition, native migration tooling and finite setup may use
public DbContexts/configuration to keep registration, saves and transactions explicit.
This limited composition exception avoids a custom persistence facade while preserving
reviewable business-call boundaries. Module implementations do not reference each other or
the host; Inventory stores the admitted Organization key without an Access database FK.

The boundary is consumer-owned sample policy. It neither changes independently adoptable
technical libraries nor prevents privileged native EF/raw SQL bypasses. See
[the approved plan](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e3-4-persisted-business-ingress.md) and
[executable module ownership](../../samples/Wholesale/modules/README.md).
