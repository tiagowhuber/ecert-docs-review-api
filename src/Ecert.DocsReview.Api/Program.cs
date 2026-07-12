using System.Text.Json.Serialization;
using Ecert.DocsReview.Api.Application;
using Ecert.DocsReview.Api.Infrastructure;
using Ecert.DocsReview.Api.Infrastructure.Pdf;
using Ecert.DocsReview.Api.Infrastructure.Storage;
using Ecert.DocsReview.Api.Infrastructure.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services
    .AddControllers()
    // Serialize enums by name ("Created", "Contract") rather than as opaque
    // integers, matching how they are stored and keeping the API readable.
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// AddOpenApi builds its schemas from these options, not from MVC's
// AddJsonOptions above, so the converter must be registered here too or the
// document would describe the enums as integers.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
// The OpenAPI document doubles as the guided tour: the transformers order the
// endpoints by lifecycle and pre-assemble the request bodies as named examples.
builder.Services.AddOpenApi(options =>
{
    // Inline enum schemas instead of $ref-ing shared components: Swagger UI
    // then shows the allowed values on every field, and the storyline
    // transformer can attach examples directly to form fields like `Type`.
    options.CreateSchemaReferenceId = info =>
        info.Type.IsEnum ? null : OpenApiOptions.CreateDefaultSchemaReferenceId(info);
    options.AddDocumentTransformer(StorylineOpenApi.TransformDocumentAsync);
    options.AddOperationTransformer(StorylineOpenApi.TransformOperationAsync);
});
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
builder.Services.AddSingleton<IPdfAnalyzer, PdfPigAnalyzer>();
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

// API docs are part of the deliverable (the examiner explores the API through
// them), so they are exposed in every environment, not just Development.
app.MapOpenApi();
app.UseSwaggerUI(options =>
    options.SwaggerEndpoint("/openapi/v1.json", "ecert Document Review API"));

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposed so the integration test project can boot the real pipeline via
// WebApplicationFactory<Program>.
public partial class Program;
