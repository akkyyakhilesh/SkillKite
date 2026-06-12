namespace SkillKite.Infrastructure.PDF;

/// <summary>
/// Verified YouTube channel URLs, matched case-insensitively against a
/// resource's title/platform text. Companion to <see cref="KnownResourceUrls"/>.
/// Used by the PDF renderer to repair hallucinated youtube.com/watch?v= links:
/// if the title names a channel we know, link the channel; otherwise fall back
/// to a YouTube search URL built from the title (see RewriteYouTubeUrl).
/// Channel handles verified 2026-06-12. Curated picks can replace these later.
/// </summary>
public static class KnownYouTubeChannels
{
    private static readonly (string Key, string Url)[] _entries =
    [
        ("freeCodeCamp",     "https://www.youtube.com/@freecodecamp"),
        ("Physics Wallah",   "https://www.youtube.com/@PhysicsWallah"),
        ("PhysicsWallah",    "https://www.youtube.com/@PhysicsWallah"),
        ("Code With Harry",  "https://www.youtube.com/@CodeWithHarry"),
        ("CodeWithHarry",    "https://www.youtube.com/@CodeWithHarry"),
        ("Apna College",     "https://www.youtube.com/@ApnaCollegeOfficial"),
        ("Khan Academy",     "https://www.youtube.com/@khanacademy"),
        ("NPTEL",            "https://www.youtube.com/@nptelhrd"),
        ("StudyIQ",          "https://www.youtube.com/@studyiq"),
        ("Study IQ",         "https://www.youtube.com/@studyiq"),
        ("Adda247",          "https://www.youtube.com/@Adda247"),
        ("Telusko",          "https://www.youtube.com/@Telusko"),
        ("Technical Guruji", "https://www.youtube.com/@TechnicalGuruji"),
        ("Unacademy",        "https://www.youtube.com/@unacademy"),
        ("Gate Smashers",    "https://www.youtube.com/@GateSmashers"),
        ("Programming with Mosh", "https://www.youtube.com/@programmingwithmosh"),
    ];

    private static readonly (string KeyLower, string Url)[] _sorted =
        _entries
            .Select(e => (KeyLower: e.Key.ToLowerInvariant(), e.Url))
            .OrderByDescending(e => e.KeyLower.Length)
            .ToArray();

    /// <summary>Channel URL for the first known channel named in <paramref name="text"/>, or null.</summary>
    public static string? LookupChannel(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var lower = text.ToLowerInvariant();
        foreach (var (keyLower, url) in _sorted)
            if (lower.Contains(keyLower)) return url;
        return null;
    }

    /// <summary>True when the URL points at a specific YouTube video — the kind Claude hallucinates.</summary>
    public static bool IsVideoUrl(string? url) =>
        !string.IsNullOrEmpty(url) &&
        (url.Contains("youtube.com/watch", StringComparison.OrdinalIgnoreCase) ||
         url.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase) ||
         url.Contains("youtube.com/shorts/", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Repairs a hallucinated video URL: prefer the verified channel if the title
    /// names one we know, else a YouTube search for the title — the search always
    /// works and surfaces a live video for the topic.
    /// </summary>
    public static string RewriteVideoUrl(string title)
    {
        var channel = LookupChannel(title);
        if (channel != null) return channel;
        return "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(title);
    }
}
