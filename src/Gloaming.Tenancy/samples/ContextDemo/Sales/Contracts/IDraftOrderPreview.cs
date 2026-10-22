namespace ModulithFoundry.Samples.Wholesale.ContextDemo.Sales.Contracts;

public interface IDraftOrderPreview
{
    DraftPreview Preview(string sku, int quantity);
}
