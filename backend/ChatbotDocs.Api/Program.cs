using System.Net.Http.Headers;
using ChatbotDocs.Api.Options;
using ChatbotDocs.Api.Services;

var builder = WebApplication.CreateBuilder(args);

const string AngularCorsPolicy = "AngularClient";

builder.Services.Configure<AnythingLlmOptions>(
    builder.Configuration.GetSection(AnythingLlmOptions.SectionName));
builder.Services.Configure<DocumentSyncOptions>(
    builder.Configuration.GetSection(DocumentSyncOptions.SectionName));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Chatbot Docs API",
        Version = "v1",
        Description = "Backend .NET 8 que conecta un frontend Angular con AnythingLLM + Ollama " +
                      "para chatear sobre documentos del servidor y generar documentos nuevos."
    });
});

builder.Services.AddSingleton<IDocumentStorageService, DocumentStorageService>();

builder.Services.AddHttpClient<IAnythingLlmClient, AnythingLlmClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AnythingLlmOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromMinutes(2);

    if (!string.IsNullOrWhiteSpace(options.ApiKey))
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
    }
});

builder.Services.AddScoped<IDocumentSyncService, DocumentSyncService>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:4200" };

builder.Services.AddCors(options =>
{
    options.AddPolicy(AngularCorsPolicy, policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(AngularCorsPolicy);
app.UseAuthorization();
app.MapControllers();

var documentSyncOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DocumentSyncOptions>>().Value;

if (documentSyncOptions.AutoSyncOnStartup)
{
    using var startupScope = app.Services.CreateScope();
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var syncService = startupScope.ServiceProvider.GetRequiredService<IDocumentSyncService>();

    foreach (var carpeta in syncService.ListAvailableCarpetas())
    {
        try
        {
            var result = await syncService.SyncAsync(carpeta, CancellationToken.None);
            startupLogger.LogInformation(
                "Sincronización inicial del caso '{Carpeta}': {New} nuevos, {Already} ya embebidos, {Errors} con error.",
                carpeta, result.NewlyEmbedded.Count, result.AlreadyEmbedded.Count, result.Errors.Count);
        }
        catch (Exception ex)
        {
            startupLogger.LogWarning(ex,
                "No se pudo sincronizar el caso '{Carpeta}' al iniciar (probablemente AnythingLLM no está " +
                "disponible todavía). Usa POST /api/documents/{{carpeta}}/sync para reintentar manualmente.", carpeta);
        }
    }
}

app.Run();
