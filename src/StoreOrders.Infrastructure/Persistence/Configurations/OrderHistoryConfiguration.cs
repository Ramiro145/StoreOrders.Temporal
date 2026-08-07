using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOrders.Domain.Entities;

namespace StoreOrders.Infrastructure.Persistence.Configurations;

public sealed class OrderHistoryConfiguration
    : IEntityTypeConfiguration<OrderHistoryEntry>
{
    public void Configure(EntityTypeBuilder<OrderHistoryEntry> builder)
    {
        builder.ToTable("OrderHistory", table =>
        {
            table.HasCheckConstraint(
                "CK_OrderHistory_CurrentStatus",
                "[CurrentStatus] IN ('Received','AwaitingPayment','Paid'," +
                "'Preparing','ReadyForShipment','Shipped'," +
                "'Delivered','Cancelled','Rejected')");
            table.HasCheckConstraint(
                "CK_OrderHistory_PreviousStatus",
                "[PreviousStatus] IS NULL OR [PreviousStatus] IN " +
                "('Received','AwaitingPayment','Paid','Preparing'," +
                "'ReadyForShipment','Shipped','Delivered'," +
                "'Cancelled','Rejected')");
            table.HasCheckConstraint(
                "CK_OrderHistory_ActorType",
                "[ActorType] IN ('System','Customer','PaymentService'," +
                "'Warehouse','DeliveryService')");
        });

        builder.HasKey(x => x.HistoryId);

        builder.Property(x => x.HistoryId).ValueGeneratedOnAdd();
        builder.Property(x => x.EventType).HasMaxLength(50).IsRequired();

        builder.Property(x => x.PreviousStatus)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(x => x.CurrentStatus)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(x => x.ActorType)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(x => x.Description).HasMaxLength(500);

        builder.Property(x => x.OperationKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.OccurredAtUtc).HasPrecision(3);

        builder.HasIndex(x => new { x.OrderId, x.OccurredAtUtc })
            .HasDatabaseName("IX_OrderHistory_OrderId_OccurredAtUtc");

        builder.HasIndex(x => x.OperationKey)
            .IsUnique()
            .HasDatabaseName("UX_OrderHistory_OperationKey");

        builder.HasOne(x => x.Order)
            .WithMany(x => x.History)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
