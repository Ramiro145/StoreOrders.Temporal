using System.Text.Json.Serialization;
using StoreOrders.Api.Options;
using StoreOrders.Api.Services;
using StoreOrders.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
    });

builder.Services.AddOpenApi();

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
    string.IsNullOrWhiteSpace(temporalOptions.Namespace))
{
    throw new InvalidOperationException(
        "La configuración de Temporal está incompleta.");
}

builder.Services.AddStoreOrdersInfrastructure(
    connectionString);

builder.Services.AddTemporalClient(
    temporalOptions.TargetHost,
    temporalOptions.Namespace);

builder.Services.AddScoped<
    IOrderWorkflowGateway,
    TemporalOrderWorkflowGateway>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
