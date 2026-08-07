using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StoreOrders.Infrastructure.Persistence;

public sealed class StoreOrdersDbContextFactory
    : IDesignTimeDbContextFactory<StoreOrdersDbContext>
{
    public StoreOrdersDbContext CreateDbContext(string[] args)
    {
        var password =
            Environment.GetEnvironmentVariable("STOREORDERS_SQL_PASSWORD")
            ?? throw new InvalidOperationException(
                "La variable STOREORDERS_SQL_PASSWORD no está definida.");

        var port =
            Environment.GetEnvironmentVariable("STOREORDERS_SQL_PORT")
            ?? "14330";

        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = $"localhost,{port}",
            InitialCatalog = "StoreOrdersDb",
            UserID = "sa",
            Password = password,
            Encrypt = true,
            TrustServerCertificate = true
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<StoreOrdersDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new StoreOrdersDbContext(options);
    }
}
