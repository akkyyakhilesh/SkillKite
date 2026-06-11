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
    private const string LatinFont = "Noto Sans";

    // Warm Indian Earth palette (spec §1)
    private static class Palette
    {
        public const string Saffron     = "E97B27";
        public const string SaffronDark = "B45309";
        public const string Indigo      = "3949AB";
        public const string Forest      = "2E7D32";
        public const string LinkBlue    = "1976D2";
        public const string CardTitle   = "1F2937";
        public const string FieldLabel  = "4B5563";
        public const string Body        = "374151";
        public const string TldrGrey    = "6B7280";
        public const string FooterGrey  = "6B7280";
        public const string PageBg      = "FAF9F6";
        public const string CardBg      = "FFFFFF";
        public const string DividerLine = "E5E7EB";
    }

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
        var fontDir = Path.Combine(AppContext.BaseDirectory, "Fonts");
        if (!Directory.Exists(fontDir)) return;
        foreach (var path in Directory.EnumerateFiles(fontDir, "*.ttf"))
        {
            using var stream = File.OpenRead(path);
            FontManager.RegisterFont(stream);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static (string Color, string Icon) SectionStyle(int index, string title)
    {
        var t = title.ToLowerInvariant();
        if (t.Contains("skill"))       return (Palette.Saffron, "🎓");
        if (t.Contains("role"))        return (Palette.Indigo,   "🎯");
        if (t.Contains("side") || t.Contains("move")) return (Palette.Forest, "🔀");
        return (index % 3) switch
        {
            0 => (Palette.Saffron, "🎓"),
            1 => (Palette.Indigo,  "🎯"),
            _ => (Palette.Forest,  "🔀"),
        };
    }

    // Appends text to an open TextDescriptor, hyperlinking any known resource spans.
    // QuestPDF 2024.10 API: TextDescriptor.Hyperlink(url, text) → TextSpanDescriptor
    private static void AppendWithLinks(TextDescriptor t, string text)
    {
        foreach (var (seg, url) in KnownResourceUrls.Segment(text))
        {
            if (url != null)
                t.Hyperlink(url, seg).FontColor(Palette.LinkBlue).Underline();
            else
                t.Span(seg);
        }
    }

    // Renders a single icon-prefixed field line inside a card.
    private static void IconField(ColumnDescriptor col, string icon, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        col.Item().PaddingTop(3).Text(t =>
        {
            t.DefaultTextStyle(x => x.FontSize(11).FontFamily(LatinFont).FontColor(Palette.Body));
            t.Span($"{icon} ");
            AppendWithLinks(t, value);
        });
    }

    // Section header row: "🎓 SKILLS TO BUILD" in section colour + optional TLDR.
    private static void RenderSectionHeader(
        ColumnDescriptor col, string icon, string title, string color,
        string? tldr, bool isContinuation)
    {
        var label = $"{icon} {title.ToUpperInvariant()}{(isContinuation ? " (continued)" : "")}";
        col.Item().Text(label)
            .FontSize(14).Bold().FontFamily(LatinFont).FontColor(color);

        if (!isContinuation && !string.IsNullOrWhiteSpace(tldr))
            col.Item().PaddingTop(2).Text(tldr!)
                .FontSize(11).Italic().FontFamily(LatinFont).FontColor(Palette.TldrGrey);

        col.Item().PaddingTop(4).PaddingBottom(6).LineHorizontal(0.5f).LineColor(Palette.DividerLine);
    }

    // One option card with left colour border (spec §4 / §7).
    private static void RenderGuideCard(
        ColumnDescriptor col, GuideOption opt, string borderColor, string sectionIcon)
    {
        col.Item()
            .BorderLeft(4).BorderColor(borderColor)
            .Background(Palette.CardBg)
            .Padding(10).PaddingLeft(12)
            .Column(oc =>
            {
                // Card title: section emoji + option name
                oc.Item().Text($"{sectionIcon} {opt.Name}")
                    .FontSize(13).SemiBold().FontFamily(LatinFont).FontColor(Palette.CardTitle);

                IconField(oc, "❓", opt.WhatIsIt);
                IconField(oc, "👤", opt.WhoFor);
                IconField(oc, "➡️", opt.LeadsTo);
                IconField(oc, "📜", opt.KeyExams);
                IconField(oc, "⏱️", opt.TimeCommitment);
            });
    }

    private static void RenderDisclaimer(ColumnDescriptor col)
    {
        col.Item().PaddingTop(16).Text(
            "Disclaimer: Yeh AI-generated guidance hai, professional counseling ki jagah nahi. " +
            "Final decision apne teachers aur family ke saath milke lein.")
            .FontSize(9).Italic().FontFamily(LatinFont).FontColor(Palette.TldrGrey);
    }

    private static void RenderPageHeader(PageDescriptor p, string tagline)
    {
        p.Header().Column(col =>
        {
            col.Item().Row(r =>
            {
                r.RelativeItem().Text("🪁 SkillKite")
                    .FontSize(18).Bold().FontFamily(LatinFont).FontColor(Palette.SaffronDark);
                r.AutoItem().AlignRight().AlignMiddle().Text(tagline)
                    .FontSize(11).FontFamily(LatinFont).FontColor(Palette.TldrGrey);
            });
            col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(Palette.SaffronDark);
        });
    }

    private static void RenderPageFooter(PageDescriptor p, string prefix)
    {
        p.Footer().AlignCenter().Text(t =>
        {
            t.Span($"{prefix} • Generated ").FontSize(9).FontFamily(LatinFont).FontColor(Palette.FooterGrey);
            t.Span(DateTime.UtcNow.ToString("yyyy-MM-dd")).FontSize(9).FontFamily(LatinFont).FontColor(Palette.FooterGrey);
            t.Span("   ").FontSize(9);
            t.CurrentPageNumber().FontSize(9).FontFamily(LatinFont).FontColor(Palette.FooterGrey);
            t.Span(" / ").FontSize(9).FontFamily(LatinFont).FontColor(Palette.FooterGrey);
            t.TotalPages().FontSize(9).FontFamily(LatinFont).FontColor(Palette.FooterGrey);
        });
    }

    // ── Public API ──────────────────────────────────────────────────────────

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
                p.Background().Background(Palette.PageBg);
                p.DefaultTextStyle(x => x.FontSize(11).FontFamily(LatinFont));

                RenderPageHeader(p, $"Career roadmap · {roadmap.TotalWeeks} weeks");

                p.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(0);

                    // Title block
                    col.Item().PaddingBottom(4).Text(
                        $"Personal Roadmap for {student.Name ?? "Student"}")
                        .FontSize(16).Bold().FontColor(Palette.CardTitle);

                    col.Item().Text(roadmap.CareerTitle)
                        .FontSize(13).SemiBold().FontColor(Palette.Saffron);

                    col.Item().PaddingTop(2).Text(roadmap.Summary)
                        .FontSize(11).FontColor(Palette.Body);

                    // Salary — visual heavyweight per spec §2
                    col.Item().PaddingTop(6).PaddingBottom(10).Text(t =>
                    {
                        t.Span("💰 Expected: ").FontSize(11).FontColor(Palette.FieldLabel).SemiBold();
                        t.Span($"₹{roadmap.ExpectedSalaryMin:N0} – ₹{roadmap.ExpectedSalaryMax:N0}/month")
                            .FontSize(14).Bold().FontColor(Palette.CardTitle);
                    });

                    col.Item().PaddingBottom(10).Text("📅 Week-by-week plan")
                        .FontSize(14).Bold().FontColor(Palette.Saffron);

                    // 3 week-cards per page (spec §5)
                    var weeks = roadmap.Weeks;
                    var chunks = weeks.Chunk(3).ToArray();
                    for (int ci = 0; ci < chunks.Length; ci++)
                    {
                        foreach (var w in chunks[ci])
                        {
                            col.Item().PaddingBottom(8)
                                .BorderLeft(4).BorderColor(Palette.Saffron)
                                .Background(Palette.CardBg)
                                .Padding(10).PaddingLeft(12)
                                .Column(wc =>
                                {
                                    wc.Item().Text($"Week {w.WeekNumber}: {w.Theme}")
                                        .FontSize(13).SemiBold().FontColor(Palette.CardTitle);

                                    wc.Item().PaddingTop(4).Text("Goals:").FontSize(11).SemiBold().FontColor(Palette.FieldLabel);
                                    foreach (var g in w.Goals)
                                        wc.Item().Text($"• {g}").FontSize(11).FontColor(Palette.Body);

                                    if (w.Resources.Count > 0)
                                    {
                                        wc.Item().PaddingTop(4);
                                        foreach (var r in w.Resources)
                                        {
                                            // Prefer a verified URL; fall back to Claude's URL
                                            var resolvedUrl = KnownResourceUrls.LookupFirst(r.Title) ?? r.Url;
                                            wc.Item().Text(t =>
                                            {
                                                t.DefaultTextStyle(x => x.FontSize(10).FontFamily(LatinFont));
                                                t.Span("🔗 ");
                                                if (!string.IsNullOrEmpty(resolvedUrl))
                                                    t.Hyperlink(resolvedUrl, $"{r.Title} ({r.Platform})")
                                                        .FontColor(Palette.LinkBlue).Underline();
                                                else
                                                    t.Span($"{r.Title} ({r.Platform})")
                                                        .FontColor(Palette.Body);
                                            });
                                        }
                                    }

                                    if (!string.IsNullOrWhiteSpace(w.Practice))
                                    {
                                        wc.Item().PaddingTop(4).Text(t =>
                                        {
                                            t.DefaultTextStyle(x => x.FontSize(11).FontFamily(LatinFont));
                                            t.Span("🛠️ Practice: ").SemiBold().FontColor(Palette.FieldLabel);
                                            t.Span(w.Practice).FontColor(Palette.Body);
                                        });
                                    }
                                });
                        }

                        bool isLastChunk = ci == chunks.Length - 1;
                        if (!isLastChunk)
                            col.Item().PageBreak();
                    }

                    // Disclaimer on last page only (spec §5)
                    RenderDisclaimer(col);
                });

                RenderPageFooter(p, "SkillKite");
            });
        }).GeneratePdf(path);

        return Task.FromResult($"{_opts.PublicBaseUrl.TrimEnd('/')}/{filename}");
    }

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
                p.Background().Background(Palette.PageBg);
                p.DefaultTextStyle(x => x.FontSize(11).FontFamily(LatinFont));

                RenderPageHeader(p, $"{guide.FlowLabel} guide");

                p.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(0);

                    col.Item().PaddingBottom(4).Text(guide.Heading)
                        .FontSize(16).Bold().FontColor(Palette.CardTitle);

                    if (!string.IsNullOrWhiteSpace(guide.Greeting))
                        col.Item().PaddingBottom(10).Text(guide.Greeting)
                            .FontSize(11).FontColor(Palette.Body);

                    for (int si = 0; si < guide.Sections.Count; si++)
                    {
                        var section = guide.Sections[si];
                        var (sectionColor, sectionIcon) = SectionStyle(si, section.Title);

                        if (si > 0)
                            col.Item().PaddingTop(22);

                        // 3 options per page; continuation pages re-show the section header
                        var chunks = section.Options.Chunk(3).ToArray();
                        for (int ci = 0; ci < chunks.Length; ci++)
                        {
                            RenderSectionHeader(col, sectionIcon, section.Title, sectionColor,
                                section.Intro, isContinuation: ci > 0);

                            foreach (var opt in chunks[ci])
                            {
                                col.Item().PaddingBottom(8);
                                RenderGuideCard(col, opt, sectionColor, sectionIcon);
                            }

                            bool isLastChunk = ci == chunks.Length - 1;
                            bool isLastSection = si == guide.Sections.Count - 1;
                            if (!isLastChunk || !isLastSection)
                            {
                                if (!isLastChunk)
                                    col.Item().PageBreak();
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(guide.ClosingMessage))
                    {
                        col.Item().PaddingTop(16)
                            .BorderLeft(4).BorderColor(Palette.Saffron)
                            .Background(Palette.CardBg)
                            .Padding(10).PaddingLeft(12)
                            .Text(guide.ClosingMessage)
                            .FontSize(11).FontColor(Palette.Body);
                    }

                    // Disclaimer on last page only (spec §5)
                    RenderDisclaimer(col);
                });

                RenderPageFooter(p, $"SkillKite · {guide.FlowLabel}");
            });
        }).GeneratePdf(path);

        return Task.FromResult($"{_opts.PublicBaseUrl.TrimEnd('/')}/{filename}");
    }

    public Task DeletePdfsForStudentAsync(Guid studentId, CancellationToken ct = default)
    {
        var dir = _opts.OutputDirectory;
        if (!Directory.Exists(dir)) return Task.CompletedTask;

        var idTag = studentId.ToString("N");
        foreach (var file in Directory.EnumerateFiles(dir, $"*{idTag}*.pdf"))
        {
            try { File.Delete(file); }
            catch { /* best-effort */ }
        }
        return Task.CompletedTask;
    }

    public (string Url, string Filename)? FindLatestPdfForStudent(Guid studentId)
    {
        var dir = _opts.OutputDirectory;
        if (!Directory.Exists(dir)) return null;

        var idTag = studentId.ToString("N");
        var latest = Directory.EnumerateFiles(dir, $"*{idTag}*.pdf")
            .Select(f => new FileInfo(f))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .FirstOrDefault();

        if (latest is null) return null;
        return ($"{_opts.PublicBaseUrl.TrimEnd('/')}/{latest.Name}", latest.Name);
    }
}
