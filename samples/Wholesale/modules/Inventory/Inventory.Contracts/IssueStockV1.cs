namespace Gloaming.Samples.Wholesale.Inventory.Contracts;

/// <summary>Consumer integration command; delivery and admitted Organization identity are envelope metadata.</summary>
public sealed record IssueStockV1(Guid StockPositionId, long ExpectedVersion, decimal Quantity);
