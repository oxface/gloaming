# B1 retained profile-feed experiment

Status: closed assessment, 2026-10-14. Preserved research/protocol evidence, excluded from
the common Wholesale sample, active solution, CI test lanes and repository formatting.
No production adoption, library extraction or bootstrap guarantee is established.

The [assessment](../../docs/reports/b1-retention-and-contract-assessment.md) records the
retention and public-contract findings. The [dated proof report](../../docs/reports/b1-replayable-integration-feed.md)
records the original PostgreSQL/process executions; those results are historical evidence
for the experiment rather than current sample guarantees.

## Preserved material

[snapshot/](snapshot/) retains the experiment's source, tests, Contracts, native migration,
modified Sales/HTTP files and solution/CI wiring at their original relative paths.
[manifest.json](manifest.json) identifies the baseline commit and SHA-256 of each preserved
file. The snapshot is incomplete as a standalone checkout: unchanged dependencies belong
to that baseline. Its original README and project paths describe the former composition;
they are preserved byte-for-byte, not active instructions for this checkout.

[implementation.patch](implementation.patch) reproduces the runtime/test composition over
baseline `d674e39a95f4aec8fa617b3382d423dc748bf166`. To investigate later, apply it in a
separate disposable checkout of that commit, restore/build there and follow the preserved
demo README. Do not apply it to the common sample as routine setup. No new process tests
are claimed for the stored snapshot itself.

## Common sample isolation

Normal Sales profile writes, DbContext mapping, registration and HTTP setup are restored
to the merged baseline. Sales.Contracts exposes no feed API. The experiment's unmerged
forward migration and model additions live only in this snapshot; earlier migration bytes
are unchanged. The diagnostic feed projects are absent from the active solution and CI.
No archived source or fixtures were changed.

The owner's staged review snapshot is preserved. Working-tree removals and restored files
represent the requested isolation and remain unstaged; stage the complete isolation change
only when reviewing it for a future commit. No commit or push was authorized.
