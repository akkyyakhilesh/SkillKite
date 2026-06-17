using System.Threading.RateLimiting;
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
// Claude roadmap generation is our heaviest call (20 weeks × resources × bilingual
// fields, plus retries on parse failures). The .NET HttpClient default of 100s
// timed out on real students — Shristi (06-06) tapped a career suggestion, the
// roadmap call ran past 100s, the request was cancelled, and she never got a PDF.
// 180s gives Claude enough headroom for the largest plans without letting a truly
// hung request block a connection forever.
builder.Services.AddHttpClient<ICareerEngine, ClaudeCareerEngine>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(180);
});
builder.Services.AddHttpClient<IMessagingService, WhatsAppService>();

// --- Services ---
builder.Services.AddSingleton<IRoadmapGenerator, RoadmapPdfGenerator>();
builder.Services.AddScoped<ICareerPathRepository, CareerPathRepository>();
builder.Services.AddScoped<AssessmentOrchestrator>();

// --- Web ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o =>
{
    o.AddPolicy("web", p => p
        .WithOrigins("https://skillkite.in", "https://www.skillkite.in", "http://localhost:4321")
        .AllowAnyHeader()
        .AllowAnyMethod());
    o.AddPolicy("webhook", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("web-chat", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Request.Headers["X-Session-Key"].FirstOrDefault() ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles(); // serves /roadmaps/*.pdf from wwwroot/roadmaps
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
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
