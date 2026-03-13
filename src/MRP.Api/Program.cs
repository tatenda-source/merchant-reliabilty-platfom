using Microsoft.EntityFrameworkCore;
using MRP.Agents.Handlers;
using MRP.Agents.Ingestion;
using MRP.Agents.Intelligence;
using MRP.Agents.Recovery;
using MRP.Domain.Interfaces;
using MRP.Infrastructure.EventBus;
using MRP.Infrastructure.Paynow;
using MRP.Infrastructure.Persistence;
using MRP.Infrastructure.Persistence.Repositories;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration));

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<MrpDbContext>(options =>
    options.UseNpgsql(connectionString));

// Repositories
builder.Services.AddScoped<IMerchantRepository, MerchantRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IReconciliationRepository, ReconciliationRepository>();
builder.Services.AddScoped<IRecoveryRepository, RecoveryRepository>();
builder.Services.AddScoped<ISettlementRepository, SettlementRepository>();
builder.Services.AddScoped<IMerchantProfileRepository, MerchantProfileRepository>();

// Paynow
builder.Services.Configure<PaynowOptions>(
    builder.Configuration.GetSection("Paynow"));
builder.Services.AddScoped<IPaynowGateway, PaynowGatewayWrapper>();

// Event Bus (MediatR) — register from both Infrastructure (bus) and Agents (handlers)
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblies(
        typeof(MrpDbContext).Assembly,
        typeof(TransactionReceivedHandler).Assembly));
builder.Services.AddScoped<IEventBus, MediatREventBus>();

// Pipeline Services
builder.Services.AddScoped<IIngestionService, IngestionService>();
builder.Services.AddScoped<IIntelligenceEngine, IntelligenceEngine>();
builder.Services.AddScoped<IRecoveryEngine, RecoveryEngine>();

// Recovery background channel (bounded, backpressure-aware)
builder.Services.Configure<RecoveryOptions>(
    builder.Configuration.GetSection("Recovery"));
builder.Services.AddSingleton<RecoveryChannel>();
builder.Services.AddHostedService<RecoveryWorker>();

// API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "MRP - Merchant Reliability Platform",
        Version = "v2",
        Description = "Event-driven Merchant Reliability Platform for Paynow Zimbabwe"
    });
});

// Health Checks
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString ?? "", name: "postgresql");

// CORS for Dashboard
builder.Services.AddCors(options =>
{
    options.AddPolicy("Dashboard", policy =>
        policy.WithOrigins("http://localhost:8081", "https://localhost:8081")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

// Apply migrations in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MrpDbContext>();
    await db.Database.MigrateAsync();
}

// Handle --migrate flag for container deployments
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MrpDbContext>();
    await db.Database.MigrateAsync();
    return;
}

app.UseSerilogRequestLogging();
app.UseCors("Dashboard");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { } // For WebApplicationFactory in tests
