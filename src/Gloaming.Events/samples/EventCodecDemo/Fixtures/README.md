# Event codec literal fixtures

Inventory `opened.v1.json` and `received.v1.json` are byte-for-byte copies of the frozen
[`StockPositionEvents` fixtures](https://github.com/oxface/gloaming/tree/15d1ec6ce85f872e4547b37c33658dea76b25d03/tests/ApplicationTests/Fixtures/StockPositionEvents)
at original checkpoint `15d1ec6`. Their durable aliases, identifiers, field names and decimal
quantity are retained. The active build/runtime uses only these local copies.

Purchasing `drafted.v1.json` and `line-set.v1.json` are new literal fixtures matching the
archived [draft/line event contract](https://github.com/oxface/gloaming/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/PurchaseOrders/Events/PurchaseOrderEvents.cs).
The draft values also appear in the archived unknown-schema/missing-field persistence proof;
there were no corresponding retained Purchasing JSON fixture files to relocate. The line
values deliberately give quantity 2.5, unit price 12.5 and independently expected total 31.25.

The envelopes contain only event name, schema version and payload. Stream identity/version,
timestamps, attribution and storage are outside the codec envelope. E5.1 supplies stream
positions/timestamps explicitly in the two history recipes; these are authored demonstration
metadata, not retained database history or commit-order evidence.

Inventory `received-second.v1.json` and Purchasing `line-replaced.v1.json` are new E5.1
literals using the existing v1 schemas. The receipt adds 2.875 and a delivery reference;
the replacement line uses quantity 5 and unit price 12.5. Independently expected current
results are on-hand 13 and order total 62.50. The four E4 payload files are unchanged.

The upcasting slice adds independently authored `Inventory/received.v2.json` and
`Purchasing/line-set.v2.json` / `line-set.v3.json`. The Inventory quantity is unchanged at
10.125 but the field is `receivedQuantity`. Purchasing v2 renames flat fields; v3 nests the
same line facts. Each line still independently implies total 31.25. These are new schemas,
not migrated archive fixtures. Every earlier v1 JSON file remains byte-for-byte unchanged.
