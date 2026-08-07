using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOrders.Domain.Entities;

namespace StoreOrders.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
        {
            table.HasCheckConstraint(
                "CK_Products_CurrentPrice",
                "[CurrentPrice] >= 0");
            table.HasCheckConstraint(
                "CK_Products_Sku_NotBlank",
                "LEN(LTRIM(RTRIM([Sku]))) > 0");
            table.HasCheckConstraint(
                "CK_Products_Name_NotBlank",
                "LEN(LTRIM(RTRIM([Name]))) > 0");
        });

        builder.HasKey(x => x.ProductId);

        builder.Property(x => x.Sku)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.CurrentPrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.CreatedAtUtc).HasPrecision(3);
        builder.Property(x => x.UpdatedAtUtc).HasPrecision(3);

        builder.HasIndex(x => x.Sku)
            .IsUnique()
            .HasDatabaseName("UX_Products_Sku");

        var seededAt = new DateTime(
            2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        builder.HasData(
            new Product
            {
                ProductId = 1,
                Sku = "LAP-001",
                Name = "Laptop básica",
                CurrentPrice = 14500.00m,
                IsActive = true,
                CreatedAtUtc = seededAt,
                UpdatedAtUtc = seededAt
            },
            new Product
            {
                ProductId = 2,
                Sku = "MOU-001",
                Name = "Mouse inalámbrico",
                CurrentPrice = 450.00m,
                IsActive = true,
                CreatedAtUtc = seededAt,
                UpdatedAtUtc = seededAt
            },
            new Product
            {
                ProductId = 3,
                Sku = "KEY-001",
                Name = "Teclado mecánico",
                CurrentPrice = 1200.00m,
                IsActive = true,
                CreatedAtUtc = seededAt,
                UpdatedAtUtc = seededAt
            });
    }
}
