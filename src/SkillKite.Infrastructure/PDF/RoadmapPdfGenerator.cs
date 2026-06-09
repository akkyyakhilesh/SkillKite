using Microsoft.Extensions.Options;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkillKite.Core.Dtos;
using SkillKite.Core.Enums;
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

                // No more pure-Hindi mode. The student's choice now is Hinglish
                // (default) or English, and Claude generates the content in
                // whichever they picked — so we just use the primary fields.
                // The schema's *Hi fields are vestigial but kept for legacy
                // sessions; PDF render no longer reads them.
                string PickLang(string en, string h) => en;

                p.Header().Column(col =>
                {
                    col.Item().Text("SkillKite").FontSize(24).Bold().FontColor(Colors.Orange.Darken2);
                    col.Item().Text("Right skills. Higher reach.").Italic().FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Orange.Lighten2);
                });

                p.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(10);

                    // Structural chrome stays in English — short, recognizable, doesn't compete
                    // with the substantive content. Same pattern as BHIM / Tally / Paytm:
                    // English navigation, Hindi body.
                    col.Item().Text($"Personal Roadmap for {student.Name ?? "Student"}").FontSize(16).Bold();
                    col.Item().Text($"Career path: {PickLang(roadmap.CareerTitle, roadmap.CareerTitleHi)}").Bold();
                    col.Item().Text(PickLang(roadmap.Summary, roadmap.SummaryHi));
                    col.Item().Text($"Duration: {roadmap.TotalWeeks} weeks  •  Expected salary: ₹{roadmap.ExpectedSalaryMin:N0}–₹{roadmap.ExpectedSalaryMax:N0}/month");

                    col.Item().PaddingTop(10).Text("Week-by-week plan").FontSize(14).Bold();

                    foreach (var w in roadmap.Weeks)
                    {
                        col.Item().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(wc =>
                        {
                            wc.Item().Text($"Week {w.WeekNumber}: {PickLang(w.Theme, w.ThemeHi)}").Bold();

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

    /// <summary>
    /// Renders the 10th/12th comprehensive guide PDF. Same SkillKite header/footer
    /// styling as the roadmap PDF but a much flatter layout — heading, greeting,
    /// then for each section: section title + intro, followed by a card per
    /// option with the five labelled blurbs (Kya hai / Kaun le / Iske baad /
    /// Exams / Time).
    /// </summary>
    public Task<string> GenerateGuideAsync(Student student, StudentGuide guide, CancellationToken ct = default)
    {
        var filename = $"guide_{guide.FlowLabel}_{student.Id:N}_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
        var path = Path.Combine(_opts.OutputDirectory, filename);

        Document.Create(doc =>
        {
            doc.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(36);
                p.DefaultTextStyle(x => x.FontSize(11).FontFamily(LatinFont));

                p.Header().Column(col =>
                {
                    col.Item().Text("SkillKite").FontSize(24).Bold().FontColor(Colors.Orange.Darken2);
                    col.Item().Text("Right skills. Higher reach.").Italic().FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Orange.Lighten2);
                });

                p.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Text(guide.Heading).FontSize(16).Bold();
                    if (!string.IsNullOrWhiteSpace(guide.Greeting))
                        col.Item().Text(guide.Greeting);

                    foreach (var section in guide.Sections)
                    {
                        col.Item().PaddingTop(10).Text(section.Title).FontSize(14).Bold().FontColor(Colors.Orange.Darken1);
                        if (!string.IsNullOrWhiteSpace(section.Intro))
                            col.Item().Text(section.Intro!).Italic().FontColor(Colors.Grey.Darken2);

                        foreach (var opt in section.Options)
                        {
                            col.Item().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(oc =>
                            {
                                oc.Item().Text(opt.Name).Bold();

                                void Field(string label, string value)
                                {
                                    if (string.IsNullOrWhiteSpace(value)) return;
                                    oc.Item().PaddingTop(3).Text(t =>
                                    {
                                        t.Span($"{label}: ").SemiBold();
                                        t.Span(value);
                                    });
                                }

                                Field("Kya hai", opt.WhatIsIt);
                                Field("Kaun le", opt.WhoFor);
                                Field("Iske baad", opt.LeadsTo);
                                Field("Exams", opt.KeyExams);
                                Field("Time", opt.TimeCommitment);
                            });
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(guide.ClosingMessage))
                    {
                        col.Item().PaddingTop(14).Border(1).BorderColor(Colors.Orange.Lighten2)
                            .Padding(10).Text(guide.ClosingMessage);
                    }

                    col.Item().PaddingTop(8).Text(
                        "Disclaimer: Yeh AI-generated guidance hai, professional counseling ki jagah nahi. " +
                        "Final decision apne teachers aur family ke saath milke lein.")
                        .FontSize(9).Italic().FontColor(Colors.Grey.Darken1);
                });

                p.Footer().AlignCenter().Text(t =>
                {
                    t.Span($"SkillKite • {guide.FlowLabel} guide • Generated ").FontSize(9).FontColor(Colors.Grey.Darken1);
                    t.Span(DateTime.UtcNow.ToString("yyyy-MM-dd")).FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf(path);

        var publicUrl = $"{_opts.PublicBaseUrl.TrimEnd('/')}/{filename}";
        return Task.FromResult(publicUrl);
    }

    public Task DeletePdfsForStudentAsync(Guid studentId, CancellationToken ct = default)
    {
        var dir = _opts.OutputDirectory;
        if (!Directory.Exists(dir)) return Task.CompletedTask;

        // Both roadmap PDFs (roadmap_<studentId>_<ts>.pdf) and guide PDFs
        // (guide_<flow>_<studentId>_<ts>.pdf) embed the student id in their
        // filename, written via Guid.ToString("N") — 32 hex chars, no dashes.
        var idTag = studentId.ToString("N");

        foreach (var file in Directory.EnumerateFiles(dir, $"*{idTag}*.pdf"))
        {
            try { File.Delete(file); }
            catch { /* best-effort — DB row goes away regardless */ }
        }
        return Task.CompletedTask;
    }
}
