# Domain shapes and EF mapping research

Research date: 2026-10-14. Primary-source review of EF Core 10 capabilities for the
owner's D1.1 feedback. The delivery-calendar example below is a design illustration,
not implemented sample behavior or an approved next slice. Documentation that also
describes EF Core 11 must not be read as proof of EF Core 10 support.

## Money and the existing boundary

The current [Money](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/Money.cs)
is an immutable value object: amount and currency determine its value; it has no
identity. Nothing about that concept requires ownership by one aggregate. Reuse
within Sales would be reasonable once its semantics agree across consumers. Its
nonnegative amount rule is currently draft-pricing policy, not a universal monetary
rule: balances, adjustments and refunds may need signed values. Moving it to a
technical library would introduce business policy without evidence of technical reuse.

[DraftOrderAggregate](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderAggregate.cs)
already differs from its EF rows: it constructs complete valid candidates and uses
Money and immutable lines. However, root plus line objects still closely resemble
root plus child rows. D1.1 proves richer aggregate behavior, not an arbitrary domain
shape mapper.

## A concrete shape that differs from its rows

Illustrative requirement: a delivery calendar owns available time windows. Its
domain state is an immutable `IntervalSet`: intervals are sorted, overlapping or
adjacent intervals merge, and subtracting a blackout can split one interval in two.
For example, subtracting `[10:00, 11:00)` from `[09:00, 12:00)` produces
`[09:00, 10:00)` and `[11:00, 12:00)`. Intervals have value equality and no durable
individual identity. The implementation could use an immutable ordered array of
interval structs or an interval-search tree; neither storage choice belongs in the
business interface.

Relational storage might instead contain a versioned calendar header and window
rows with storage keys, start and end. Loading builds and validates the complete
IntervalSet; accepted changes flatten it back into rows. Row keys do not become
domain entities merely because persistence needs keys. This is a proposed adapter
design, not evidence that EF cannot store calendars.

## What EF can and cannot map directly

EF supports private constructors, explicitly mapped read-only scalar properties
and fields. Encapsulation alone does not justify separate persistence objects.
However, EF cannot pass collection/reference navigations into the entity constructor;
that limits direct materialization through a constructor requiring the complete
calendar before any valid aggregate instance can exist.
[Microsoft constructor mapping](https://learn.microsoft.com/en-us/ef/core/modeling/constructors).

Entity collection navigations require an underlying `ICollection<T>` with working
`Add` and reference equality. Read-only public views and private backing collections
are supported. An immutable value set or array is therefore not directly the same
thing as a tracked entity navigation; changing its public visibility does not remove
that underlying requirement.
[Microsoft navigation mapping](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/navigations#collection-types).

EF 10 supports complex values in columns and JSON, including complex collections
inside JSON, but not collections of structs. Relational complex collections require
JSON rather than separate child tables. A calendar can choose compatible classes
and JSON, or owned/entity rows, instead of keeping the exact proposed representation.
These are tradeoffs, not a blanket inability to persist value objects.
[EF 10 release notes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew#complex-types),
[Microsoft complex-type limitations](https://learn.microsoft.com/en-us/ef/core/modeling/complex-types#limitations).

A custom value converter can serialize an IntervalSet into one column. A converter
cannot expand one property into multiple columns, and LINQ cannot query into its
converted members. Native complex JSON mapping has different query capabilities;
serializing through a converter is not equivalent to it.
[Microsoft value-conversion limitations](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions#limitations).

## Decision and evidence boundary

The sources support neither a claim that EF handles 99% of aggregate shapes nor a
requirement for separate rows in every aggregate. Choose native mapping when it
preserves useful domain semantics; choose an explicit row adapter when preserving
the aggregate's representation and complete-state construction outweighs mapping
work. SQL query needs, database constraints and provider support affect that choice.

This review changes documentation only. It runs no new executable tests and proves
no new reusable mechanism. A future calendar proof would need round-trip semantic
equivalence, merge/split behavior, invalid-state rejection, and root concurrency;
none is supplied by this research note. No library or template change is proposed.
