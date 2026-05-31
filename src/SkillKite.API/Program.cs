using Microsoft.EntityFrameworkCore;
using SkillKite.API.Middleware;
using SkillKite.Core.Interfaces;
using SkillKite.Data;
using SkillKite.Data.Seed;
using SkillKite.Infrastructure.AI;
using SkillKite.Infrastructure.Configuration;
using SkillKite.Infrastructure.Data;
using SkillKite.Infrastructure.Messaging;
using SkillKite.Infrastructure.PDF;

var builder = WebApplication.CreateBuilder(args);

// --- Options ---
builder.Services.Configure<ClaudeOptions>(builder.Configuration.GetSection(ClaudeOptions.SectionName));
builder.Services.Configure<WhatsAppOptions>(builder.Configuration.GetSection(WhatsAppOptions.SectionName));
builder.Services.Configure<PdfOptions>(builder.Configuration.GetSection(PdfOptions.SectionName));

// --- DB ---
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// --- HTTP clients for external APIs ---
builder.Services.AddHttpClient<ICareerEngine, ClaudeCareerEngine>();
builder.Services.AddHttpClient<IMessagingService, WhatsAppService>();

// --- Services ---
builder.Services.AddSingleton<IRoadmapGenerator, RoadmapPdfGenerator>();
builder.Services.AddScoped<ICareerPathRepository, CareerPathRepository>();
builder.Services.AddScoped<AssessmentOrchestrator>();

// --- Web ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles(); // serves /roadmaps/*.pdf from wwwroot/roadmaps
app.UseCors();
app.UseRouting();
app.UseMiddleware<WhatsAppSignatureValidator>();
app.MapControllers();

app.MapGet("/", () => Results.Ok(new { app = "SkillKite API", status = "ok" }));

// Apply migrations and seed curated career paths at startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        await CareerPathSeed.SeedAsync(db);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Skipping seed (DB likely unreachable in dev).");
    }
}

app.Run();
