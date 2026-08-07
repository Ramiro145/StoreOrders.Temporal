using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOrders.Domain.Entities;

namespace StoreOrders.Infrastructure.Persistence.Configurations;

public sealed class InventoryStockConfiguration
    : IEntityTypeConfiguration<InventoryStock>
{
    public void Configure(EntityTypeBuilder<InventoryStock> builder)
    {
        builder.ToTable("InventoryStocks", table =>
        {
            table.HasCheckConstraint(
                "CK_InventoryStocks_AvailableQuantity",
                "[AvailableQuantity] >= 0");
            table.HasCheckConstraint(
                "CK_InventoryStocks_ReservedQuantity",
                "[ReservedQuantity] >= 0");
        });

        builder.HasKey(x => x.ProductId);

        builder.Property(x => x.UpdatedAtUtc).HasPrecision(3);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.Product)
            .WithOne(x => x.InventoryStock)
            .HasForeignKey<InventoryStock>(x => x.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        var seededAt = new DateTime(
            2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        builder.HasData(
            new InventoryStock
            {
                ProductId = 1,
                AvailableQuantity = 5,
                ReservedQuantity = 0,
                UpdatedAtUtc = seededAt
            },
            new InventoryStock
            {
                ProductId = 2,
                AvailableQuantity = 20,
                ReservedQuantity = 0,
                UpdatedAtUtc = seededAt
            },
            new InventoryStock
            {
                ProductId = 3,
                AvailableQuantity = 8,
                ReservedQuantity = 0,
                UpdatedAtUtc = seededAt
            });
    }
}
