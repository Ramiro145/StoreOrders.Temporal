using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOrders.Domain.Entities;

namespace StoreOrders.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", table =>
        {
            table.HasCheckConstraint(
                "CK_Orders_TotalAmount",
                "[TotalAmount] > 0");
            table.HasCheckConstraint(
                "CK_Orders_Currency",
                "LEN([Currency]) = 3");
            table.HasCheckConstraint(
                "CK_Orders_Status",
                "[Status] IN ('Received','AwaitingPayment','Paid'," +
                "'Preparing','ReadyForShipment','Shipped'," +
                "'Delivered','Cancelled','Rejected')");
        });

        builder.HasKey(x => x.OrderId);

        builder.Property(x => x.OrderNumber)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.ClientRequestId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.TemporalWorkflowId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(x => x.CustomerName)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.CustomerEmail)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(x => x.Currency)
            .HasColumnType("char(3)")
            .IsRequired();

        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.CreatedAtUtc).HasPrecision(3);
        builder.Property(x => x.UpdatedAtUtc).HasPrecision(3);
        builder.Property(x => x.CancelledAtUtc).HasPrecision(3);
        builder.Property(x => x.DeliveredAtUtc).HasPrecision(3);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.OrderNumber)
            .IsUnique()
            .HasDatabaseName("UX_Orders_OrderNumber");

        builder.HasIndex(x => x.ClientRequestId)
            .IsUnique()
            .HasDatabaseName("UX_Orders_ClientRequestId");

        builder.HasIndex(x => x.TemporalWorkflowId)
            .IsUnique()
            .HasDatabaseName("UX_Orders_TemporalWorkflowId");

        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc })
            .HasDatabaseName("IX_Orders_Status_CreatedAtUtc");
    }
}
