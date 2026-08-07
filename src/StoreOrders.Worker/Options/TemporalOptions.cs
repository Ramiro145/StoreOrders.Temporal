namespace StoreOrders.Worker.Options;

public sealed class TemporalOptions
{
    public const string SectionName = "Temporal";

    public string TargetHost { get; init; } = string.Empty;

    public string Namespace { get; init; } = string.Empty;

    public string TaskQueue { get; init; } = string.Empty;
}
