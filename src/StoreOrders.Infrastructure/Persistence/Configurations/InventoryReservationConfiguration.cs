using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOrders.Domain.Entities;

namespace StoreOrders.Infrastructure.Persistence.Configurations;

public sealed class InventoryReservationConfiguration
    : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("InventoryReservations", table =>
        {
            table.HasCheckConstraint(
                "CK_InventoryReservations_Quantity",
                "[Quantity] > 0");
            table.HasCheckConstraint(
                "CK_InventoryReservations_Status",
                "[Status] IN ('Active','Released','Consumed')");
        });

        builder.HasKey(x => x.ReservationId);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.OperationKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc).HasPrecision(3);
        builder.Property(x => x.ReleasedAtUtc).HasPrecision(3);
        builder.Property(x => x.ConsumedAtUtc).HasPrecision(3);

        builder.HasIndex(x => x.OrderItemId)
            .IsUnique()
            .HasDatabaseName("UX_InventoryReservations_OrderItemId");

        builder.HasIndex(x => x.OperationKey)
            .IsUnique()
            .HasDatabaseName("UX_InventoryReservations_OperationKey");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("IX_InventoryReservations_Status");

        builder.HasOne(x => x.OrderItem)
            .WithOne(x => x.InventoryReservation)
            .HasForeignKey<InventoryReservation>(x => x.OrderItemId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
