using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StoreOrders.Infrastructure;
using StoreOrders.Worker.Options;
using StoreOrders.Workflows.Activities;
using StoreOrders.Workflows.Configuration;
using StoreOrders.Workflows.Orders;
using Temporalio.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("StoreOrders");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:StoreOrders no está configurada.");
}

var temporalOptions = builder.Configuration
    .GetRequiredSection(TemporalOptions.SectionName)
    .Get<TemporalOptions>()
    ?? throw new InvalidOperationException(
        "No se pudo cargar la configuración de Temporal.");

if (string.IsNullOrWhiteSpace(temporalOptions.TargetHost) ||
    string.IsNullOrWhiteSpace(temporalOptions.Namespace) ||
    string.IsNullOrWhiteSpace(temporalOptions.TaskQueue))
{
    throw new InvalidOperationException(
        "La configuración de Temporal está incompleta.");
}

if (!string.Equals(
        temporalOptions.TaskQueue,
        TemporalNames.TaskQueue,
        StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        $"La Task Queue configurada debe ser " +
        $"'{TemporalNames.TaskQueue}'.");
}

builder.Services.AddStoreOrdersInfrastructure(connectionString);

builder.Services
    .AddHostedTemporalWorker(
        temporalOptions.TargetHost,
        temporalOptions.Namespace,
        temporalOptions.TaskQueue)
    .AddScopedActivities<OrderActivities>()
    .AddWorkflow<OrderWorkflow>();

var host = builder.Build();

await host.RunAsync();
