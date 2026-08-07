using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoreOrders.Domain.Abstractions;
using StoreOrders.Infrastructure.Operations;
using StoreOrders.Infrastructure.Persistence;
using StoreOrders.Infrastructure.Queries;
namespace StoreOrders.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddStoreOrdersInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "La cadena de conexión es obligatoria.",
                nameof(connectionString));
        }

        services.AddDbContext<StoreOrdersDbContext>(
            options => options.UseSqlServer(connectionString));

        services.AddScoped<IOrderOperations, EfOrderOperations>();
        services.AddScoped<IOrderReadService, EfOrderReadService>();

        return services;
    }
}
