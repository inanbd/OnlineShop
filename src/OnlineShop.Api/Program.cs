using OnlineShop.Api;
using OnlineShop.Api.Endpoints;
using OnlineShop.Api.Tenancy;
using OnlineShop.Application;
using OnlineShop.Application.Abstractions;
using OnlineShop.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

var tenancyOptions = builder.Configuration
    .GetSection(TenancyOptions.SectionName)
    .Get<TenancyOptions>() ?? new TenancyOptions();

if (tenancyOptions.AllowHeaderTenantResolution && !builder.Environment.IsDevelopment())
{
    // A caller-supplied header must never establish tenant identity outside
    // development: anyone could read or write any tenant's data by changing it.
    throw new InvalidOperationException(
        $"'{TenancyOptions.SectionName}:AllowHeaderTenantResolution' is enabled in the " +
        $"'{builder.Environment.EnvironmentName}' environment. Header-based tenant resolution is a " +
        "development-only convenience and would be an authorisation bypass here.");
}

builder.Services.AddSingleton(tenancyOptions);
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

builder.Services.AddApplication();

// The two connection strings. `ReadConnection` may point at a read replica;
// `WriteConnection` always points at the primary.
builder.Services.AddPersistence(options =>
{
    options.ReadConnection =
        builder.Configuration.GetConnectionString(PersistenceOptions.ReadConnectionName) ?? string.Empty;

    options.WriteConnection =
        builder.Configuration.GetConnectionString(PersistenceOptions.WriteConnectionName) ?? string.Empty;

    options.EnforceCqrsConnectionRule =
        builder.Configuration.GetValue("Persistence:EnforceCqrsConnectionRule", defaultValue: true);
});

var app = builder.Build();

app.UseOnlineShopExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapCatalogEndpoints();
app.MapOrderEndpoints();
app.MapShopEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithTags("Diagnostics")
    .ExcludeFromDescription();

// Says plainly at startup whether read and write traffic are actually separated.
app.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("OnlineShop.Persistence")
    .LogConnectionTopology(app.Services.GetRequiredService<PersistenceOptions>());

app.Run();

/// <summary>Exposed so the architecture tests can reference this assembly.</summary>
public partial class Program;
