using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOrders.Domain.Entities;

namespace StoreOrders.Infrastructure.Persistence.Configurations;

public sealed class OrderItemConfiguration
    : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", table =>
        {
            table.HasCheckConstraint(
                "CK_OrderItems_Quantity",
                "[Quantity] > 0");
            table.HasCheckConstraint(
                "CK_OrderItems_UnitPrice",
                "[UnitPrice] >= 0");
            table.HasCheckConstraint(
                "CK_OrderItems_LineTotal",
                "[LineTotal] >= 0");
        });

        builder.HasKey(x => x.OrderItemId);

        builder.Property(x => x.ProductSku).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ProductName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.LineTotal).HasPrecision(18, 2);

        builder.HasIndex(x => x.OrderId)
            .HasDatabaseName("IX_OrderItems_OrderId");

        builder.HasIndex(x => x.ProductId)
            .HasDatabaseName("IX_OrderItems_ProductId");

        builder.HasIndex(x => new { x.OrderId, x.ProductId })
            .IsUnique()
            .HasDatabaseName("UX_OrderItems_OrderId_ProductId");

        builder.HasOne(x => x.Order)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Product)
            .WithMany(x => x.OrderItems)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
