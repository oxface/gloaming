# Receipt deployment proof consumers

Earlier and Current are separately compiled test-owned applications using the first public
receipt implementation. They have different request/result DTOs and version readers; they are
not two released library versions. Their native context owns the `deployment.logical_actions`
table and its business effect table, independently of Sales and Messaging.

Earlier writes result V1. Current retains the V1 comparison encoding despite an additive
diagnostic request field, reads V1/V2 and writes V2 for new actions. Earlier rejects V2 results
without entering business execution. No feature guesses whether changed input means the same
intent. Readers roll out before writers; restoring Current recovers a result an old reader refused.

Finite setup creates a disposable native schema. Test-only barriers pause before/after commit;
the parent coordinates overlapping processes or kills its own child to prove recovery.
The frozen V1 advisory-lock vector is held through native SQL independently of the library.
The [suite](../../CommandReceiptTests/CommandReceiptTests.csproj) also checks that the independent
business effect count stays one. No runtime host, broker or automatic infrastructure lifecycle
is extracted from these fixtures.
