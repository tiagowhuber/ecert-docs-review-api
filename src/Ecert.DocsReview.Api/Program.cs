using System.Text.Json.Serialization;
using Ecert.DocsReview.Api.Application;
using Ecert.DocsReview.Api.Infrastructure;
using Ecert.DocsReview.Api.Infrastructure.Pdf;
using Ecert.DocsReview.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services
    .AddControllers()
    // Serialize enums by name ("Created", "Contract") rather than as opaque
    // integers, matching how they are stored and keeping the API readable.
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default configuration.");

// Under the Testing environment the integration tests register their own
// SQLite DbContext; registering Npgsql here too would put two EF providers in
// the same container ("only a single database provider can be registered").
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
}

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddSingleton<IPdfAnalyzer, NullPdfAnalyzer>();
builder.Services.AddScoped<DocumentService>();

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString);

var app = builder.Build();

// Apply pending migrations and seed initial data on startup, so
// `docker compose up` brings up a ready-to-use database. Skipped under the
// Testing environment, where the integration tests own schema creation on a
// SQLite connection the Npgsql migrations can't run against.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var storageRoot = app.Configuration["Storage:RootPath"]
        ?? throw new InvalidOperationException("Missing Storage:RootPath configuration.");
    await DataSeeder.SeedAsync(db, storageRoot);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposed so the integration test project can boot the real pipeline via
// WebApplicationFactory<Program>.
public partial class Program;
