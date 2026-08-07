namespace StoreOrders.Api.Options;

public sealed class TemporalOptions
{
    public const string SectionName = "Temporal";

    public string TargetHost { get; init; } = string.Empty;

    public string Namespace { get; init; } = string.Empty;
}
