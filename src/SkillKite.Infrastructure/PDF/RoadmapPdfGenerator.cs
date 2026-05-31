using Microsoft.Extensions.Options;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkillKite.Core.Dtos;
using SkillKite.Core.Interfaces;
using SkillKite.Core.Models;
using SkillKite.Infrastructure.Configuration;

namespace SkillKite.Infrastructure.PDF;

public class RoadmapPdfGenerator : IRoadmapGenerator
{
    // Font family names we use throughout the document. The first one is the
    // primary (Latin) face; the second is registered as a glyph fallback so
    // Devanagari characters render correctly when mixed with English.
    private const string LatinFont = "Noto Sans";

    private readonly PdfOptions _opts;

    static RoadmapPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        RegisterFonts();
    }

    public RoadmapPdfGenerator(IOptions<PdfOptions> opts)
    {
        _opts = opts.Value;
        Directory.CreateDirectory(_opts.OutputDirectory);
    }

    private static void RegisterFonts()
    {
        // Fonts/ folder is copied next to the app DLL by the .csproj.
        var baseDir = AppContext.BaseDirectory;
        var fontDir = Path.Combine(baseDir, "Fonts");
        if (!Directory.Exists(fontDir)) return;

        foreach (var path in Directory.EnumerateFiles(fontDir, "*.ttf"))
        {
            using var stream = File.OpenRead(path);
            FontManager.RegisterFont(stream);
        }
    }

    public Task<string> GenerateAsync(Student student, GeneratedRoadmap roadmap, CancellationToken ct = default)
    {
        var filename = $"roadmap_{student.Id:N}_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
        var path = Path.Combine(_opts.OutputDirectory, filename);

        Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(36);

                // Primary font is Noto Sans (Latin). Noto Sans Devanagari is
                // also registered via FontManager — QuestPDF 2024.10+ resolves
                // missing glyphs automatically by walking registered fonts, so
                // Devanagari characters render with matching weight/style.
                p.DefaultTextStyle(x => x.FontSize(11).FontFamily(LatinFont));

                p.Header().Column(col =>
                {
                    col.Item().Text("SkillKite").FontSize(24).Bold().FontColor(Colors.Orange.Darken2);
                    col.Item().Text("Apne hunar ki patang udao").Italic().FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Orange.Lighten2);
                });

                p.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Text($"Personal Roadmap for {student.Name ?? "Student"}").FontSize(16).Bold();
                    col.Item().Text($"Career path: {roadmap.CareerTitle}  ({roadmap.CareerTitleHi})").Bold();
                    col.Item().Text(roadmap.Summary);
                    // Devanagari renders cleanly upright; italicizing it looks broken,
                    // so keep the Hindi summary in regular weight, just slightly dimmer.
                    col.Item().Text(roadmap.SummaryHi).FontColor(Colors.Grey.Darken2);
                    col.Item().Text($"Duration: {roadmap.TotalWeeks} weeks  •  Expected salary: ₹{roadmap.ExpectedSalaryMin:N0}–₹{roadmap.ExpectedSalaryMax:N0}/month");

                    col.Item().PaddingTop(10).Text("Week-by-week plan").FontSize(14).Bold();

                    foreach (var w in roadmap.Weeks)
                    {
                        col.Item().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(wc =>
                        {
                            wc.Item().Text($"Week {w.WeekNumber}: {w.Theme}").Bold();
                            if (!string.IsNullOrWhiteSpace(w.ThemeHi))
                                wc.Item().Text(w.ThemeHi).FontColor(Colors.Grey.Darken2);

                            wc.Item().PaddingTop(4).Text("Goals:").SemiBold();
                            foreach (var g in w.Goals)
                                wc.Item().Text($"• {g}");

                            if (w.Resources.Count > 0)
                            {
                                wc.Item().PaddingTop(4).Text("Resources:").SemiBold();
                                foreach (var r in w.Resources)
                                    wc.Item().Text($"→ {r.Title} ({r.Platform}) — {r.Url}").FontSize(9);
                            }

                            if (!string.IsNullOrWhiteSpace(w.Practice))
                                wc.Item().PaddingTop(4).Text($"Practice: {w.Practice}");
                        });
                    }
                });

                p.Footer().AlignCenter().Text(t =>
                {
                    t.Span("SkillKite • Generated ").FontSize(9).FontColor(Colors.Grey.Darken1);
                    t.Span(DateTime.UtcNow.ToString("yyyy-MM-dd")).FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf(path);

        var publicUrl = $"{_opts.PublicBaseUrl.TrimEnd('/')}/{filename}";
        return Task.FromResult(publicUrl);
    }
}
