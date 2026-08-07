using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOrders.Domain.Entities;

namespace StoreOrders.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table =>
        {
            table.HasCheckConstraint(
                "CK_Payments_Amount",
                "[Amount] > 0");
            table.HasCheckConstraint(
                "CK_Payments_Currency",
                "LEN([Currency]) = 3");
            table.HasCheckConstraint(
                "CK_Payments_Status",
                "[Status] = 'Confirmed'");
        });

        builder.HasKey(x => x.PaymentId);

        builder.Property(x => x.ExternalPaymentReference)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Amount).HasPrecision(18, 2);

        builder.Property(x => x.Currency)
            .HasColumnType("char(3)")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.OperationKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.ConfirmedAtUtc).HasPrecision(3);
        builder.Property(x => x.CreatedAtUtc).HasPrecision(3);

        builder.HasIndex(x => x.OrderId)
            .IsUnique()
            .HasDatabaseName("UX_Payments_OrderId");

        builder.HasIndex(x => x.ExternalPaymentReference)
            .IsUnique()
            .HasDatabaseName("UX_Payments_ExternalPaymentReference");

        builder.HasIndex(x => x.OperationKey)
            .IsUnique()
            .HasDatabaseName("UX_Payments_OperationKey");

        builder.HasOne(x => x.Order)
            .WithOne(x => x.Payment)
            .HasForeignKey<Payment>(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
