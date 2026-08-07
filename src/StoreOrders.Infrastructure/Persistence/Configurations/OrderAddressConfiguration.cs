using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOrders.Domain.Entities;

namespace StoreOrders.Infrastructure.Persistence.Configurations;

public sealed class OrderAddressConfiguration
    : IEntityTypeConfiguration<OrderAddress>
{
    public void Configure(EntityTypeBuilder<OrderAddress> builder)
    {
        builder.ToTable("OrderAddresses", table =>
        {
            table.HasCheckConstraint(
                "CK_OrderAddresses_AddressVersion",
                "[AddressVersion] > 0");
            table.HasCheckConstraint(
                "CK_OrderAddresses_CountryCode",
                "LEN([CountryCode]) = 2");
        });

        builder.HasKey(x => x.OrderId);

        builder.Property(x => x.RecipientName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Line1).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Line2).HasMaxLength(200);
        builder.Property(x => x.City).HasMaxLength(100).IsRequired();
        builder.Property(x => x.State).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PostalCode).HasMaxLength(20).IsRequired();

        builder.Property(x => x.CountryCode)
            .HasColumnType("char(2)")
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc).HasPrecision(3);

        builder.HasOne(x => x.Order)
            .WithOne(x => x.Address)
            .HasForeignKey<OrderAddress>(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
