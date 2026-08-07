using Microsoft.EntityFrameworkCore;
using StoreOrders.Domain.Entities;

namespace StoreOrders.Infrastructure.Persistence;

public sealed class StoreOrdersDbContext(
    DbContextOptions<StoreOrdersDbContext> options)
    : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryStock> InventoryStocks => Set<InventoryStock>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderAddress> OrderAddresses => Set<OrderAddress>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<InventoryReservation> InventoryReservations =>
        Set<InventoryReservation>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OrderFulfillment> OrderFulfillments => Set<OrderFulfillment>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<OrderHistoryEntry> OrderHistory => Set<OrderHistoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(StoreOrdersDbContext).Assembly);
    }
}
