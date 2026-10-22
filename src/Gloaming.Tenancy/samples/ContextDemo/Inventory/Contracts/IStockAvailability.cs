namespace ModulithFoundry.Samples.Wholesale.ContextDemo.Inventory.Contracts;

public interface IStockAvailability
{
    int GetAvailableQuantity(string sku);
}
