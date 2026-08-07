using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOrders.Domain.Entities;

namespace StoreOrders.Infrastructure.Persistence.Configurations;

public sealed class OrderFulfillmentConfiguration
    : IEntityTypeConfiguration<OrderFulfillment>
{
    public void Configure(EntityTypeBuilder<OrderFulfillment> builder)
    {
        builder.ToTable("OrderFulfillments", table =>
        {
            table.HasCheckConstraint(
                "CK_OrderFulfillments_Status",
                "[Status] IN ('Pending','Preparing','Packed','Cancelled')");
        });

        builder.HasKey(x => x.FulfillmentId);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.PackedBy).HasMaxLength(100);
        builder.Property(x => x.OperationKey).HasMaxLength(200);
        builder.Property(x => x.CreatedAtUtc).HasPrecision(3);
        builder.Property(x => x.PackedAtUtc).HasPrecision(3);

        builder.HasIndex(x => x.OrderId)
            .IsUnique()
            .HasDatabaseName("UX_OrderFulfillments_OrderId");

        builder.HasIndex(x => x.OperationKey)
            .IsUnique()
            .HasFilter("[OperationKey] IS NOT NULL")
            .HasDatabaseName("UX_OrderFulfillments_OperationKey");

        builder.HasOne(x => x.Order)
            .WithOne(x => x.Fulfillment)
            .HasForeignKey<OrderFulfillment>(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
