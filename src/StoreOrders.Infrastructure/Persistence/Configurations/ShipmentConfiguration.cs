using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOrders.Domain.Entities;

namespace StoreOrders.Infrastructure.Persistence.Configurations;

public sealed class ShipmentConfiguration
    : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipments", table =>
        {
            table.HasCheckConstraint(
                "CK_Shipments_Status",
                "[Status] IN ('Pending','Shipped','Delivered','Cancelled')");
        });

        builder.HasKey(x => x.ShipmentId);

        builder.Property(x => x.DeliveryWorkflowId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Carrier).HasMaxLength(100);
        builder.Property(x => x.TrackingNumber).HasMaxLength(100);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.CreatedAtUtc).HasPrecision(3);
        builder.Property(x => x.ShippedAtUtc).HasPrecision(3);
        builder.Property(x => x.DeliveredAtUtc).HasPrecision(3);

        builder.HasIndex(x => x.OrderId)
            .IsUnique()
            .HasDatabaseName("UX_Shipments_OrderId");

        builder.HasIndex(x => x.DeliveryWorkflowId)
            .IsUnique()
            .HasDatabaseName("UX_Shipments_DeliveryWorkflowId");

        builder.HasIndex(x => x.TrackingNumber)
            .IsUnique()
            .HasFilter("[TrackingNumber] IS NOT NULL")
            .HasDatabaseName("UX_Shipments_TrackingNumber");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("IX_Shipments_Status");

        builder.HasOne(x => x.Order)
            .WithOne(x => x.Shipment)
            .HasForeignKey<Shipment>(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
