using DataAPI.Infrastructure;
using DataAPI.Services;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Session;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<RavenDbOptions>(
    builder.Configuration.GetSection(RavenDbOptions.SectionName));

builder.Services.AddSingleton<IRavenDbService, RavenDbService>();

builder.Services.AddHttpClient<RecommendationAnalysisService>();
builder.Services.AddScoped<RecommendationPreparationService>();
builder.Services.AddHttpClient<RecommendationAnalysisService>();

builder.Services.Configure<IntegrationSettings>(
    builder.Configuration.GetSection(IntegrationSettings.SectionName));

builder.Services.AddSingleton<IntegrationSettingsStore>();
builder.Services.AddSingleton<FileImportReader>();
builder.Services.AddScoped<DataImportService>();
builder.Services.AddHostedService<ImportSchedulerService>();

builder.Configuration.AddJsonFile(
    "integration-settings.json",
    optional: true,
    reloadOnChange: true);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var raven = scope.ServiceProvider.GetRequiredService<IRavenDbService>();

    using var session = raven.OpenSession();

    session.Advanced.DocumentStore.Maintenance.ForDatabase("ProductsDb")
        .Send(new GetStatisticsOperation());
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();