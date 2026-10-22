namespace ModulithFoundry.Samples.Wholesale.EventCodecDemo.Purchasing;

internal interface IPurchaseOrderEvent;

internal sealed record PurchaseOrderDrafted(string Code, string SupplierReference, string Currency)
    : IPurchaseOrderEvent;

internal sealed record PurchaseOrderLineSet(string ItemCode, decimal Quantity, decimal UnitPrice)
    : IPurchaseOrderEvent;
