namespace StoreOrders.Tests;

[CollectionDefinition(
    "StoreOrders integration",
    DisableParallelization = true)]
public sealed class IntegrationTestCollection
{
    public const string Name = "StoreOrders integration";
}
