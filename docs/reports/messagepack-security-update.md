# MessagePack security update for Aspire consumers

Prepared on 2026-10-17 after RLS checkpoint `24cd3c2`, on `feat/sales-postgres-rls`.

## Outcome

A normal audited solution restore failed with NU1902 in the Wholesale AppHost and runtime
tests. `dotnet nuget why` traced MessagePack 2.5.302 through Aspire 13.6.1 and StreamJsonRpc
2.25.29. The [maintainer advisory](https://github.com/MessagePack-CSharp/MessagePack-CSharp/security/advisories/GHSA-qhrr-8q5h-9q3h)
identifies 2.5.303 as the patched 2.x release;
the [release notes](https://github.com/MessagePack-CSharp/MessagePack-CSharp/releases/tag/v2.5.303)
record the nested-array allocation security fix.

[Central package versions](../../Directory.Packages.props) now declare MessagePack 2.5.303.
An explicit [AppHost reference](../../samples/Wholesale/AppHost/Wholesale.AppHost.csproj)
raises the minimum version for both affected dependency graphs, including runtime tests
through their existing project reference. No direct reference is needed in the test project.
Aspire, StreamJsonRpc, SDK versions, audit configuration and warnings-as-errors remain unchanged.
This replaces the per-invocation audit workaround recorded in earlier RLS reports; those
reports retain their dated evidence.

## Verification

New results for this dependency change:

| Check | Result |
| --- | --- |
| Audited solution restore, `dotnet restore ModulithFoundry.slnx --force-evaluate` | Passed with normal audit settings and no audit suppression. |
| Direct/transitive vulnerability scan | All 88 solution projects checked against NuGet.org; no vulnerable packages reported. This is the feed's current assessment, not a permanent security certification. |
| Resolved graph comparison | Only MessagePack and MessagePack.Annotations changed, from 2.5.302 to 2.5.303, in AppHost/runtime tests. All other captured project package inventories matched. |
| `dotnet-inspect` API comparison | No API changes detected between MessagePack 2.5.302 and 2.5.303. |
| Full solution build, `--no-restore -m:1` | Passed with zero warnings/errors. |
| Architecture suite | 96 passed, zero skipped. |
| Existing Aspire runtime/browser suite | Six passed, zero skipped, in 4m23s: ordinary and RLS graphs plus four real OIDC browser cases. |
| Changed XML formatting | CSharpier checked both changed dependency files successfully. |

**102 tests passed** in the two final suite runs. Existing native tests started disposable
Aspire graphs and exercised setup, database outage, restricted RLS credentials and browser
journeys with the patched dependency. No additional lifecycle or deployment mode is claimed.
Both local report links resolved and changed-file whitespace checks passed. The index and
staged diff match their pre-edit snapshots; all three changed/new files remain unstaged.

## Ownership and remaining work

This is a consumer dependency remediation. No Gloaming interface, implementation, tenant/RLS
policy, template output or new reusable mechanism was introduced or proven. Existing tests
exercise the affected consumer; no implementation-mirroring test was added.

The explicit reference can be reconsidered when the selected Aspire/StreamJsonRpc dependencies
require a patched version themselves. This change does not claim an application-level exploit
or a general dependency audit beyond the recorded scan. Older RLS proof results are historical
and are not counted as new verification. Changes remain unstaged for owner review; no commit,
push, pull request or merge is included.
