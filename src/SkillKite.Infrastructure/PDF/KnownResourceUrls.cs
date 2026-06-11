namespace SkillKite.Infrastructure.PDF;

/// <summary>
/// 33-entry lookup table for known certification / exam / platform URLs.
/// Used by the PDF renderer to turn plain-text resource mentions into hyperlinks.
/// Match is case-insensitive; longest key wins when multiple keys overlap.
/// </summary>
public static class KnownResourceUrls
{
    private static readonly (string Key, string Url)[] _entries =
    [
        // Tech certs
        ("Google Data Analytics",            "https://www.coursera.org/professional-certificates/google-data-analytics"),
        ("Microsoft PL-300",                 "https://learn.microsoft.com/en-us/certifications/power-bi-data-analyst-associate/"),
        ("Power BI",                         "https://learn.microsoft.com/en-us/certifications/power-bi-data-analyst-associate/"),
        ("AWS Solutions Architect Associate","https://aws.amazon.com/certification/certified-solutions-architect-associate/"),
        ("AWS Cloud Practitioner",           "https://aws.amazon.com/certification/certified-cloud-practitioner/"),
        ("Azure AZ-900",                     "https://learn.microsoft.com/en-us/certifications/azure-fundamentals/"),
        ("Azure Fundamentals",               "https://learn.microsoft.com/en-us/certifications/azure-fundamentals/"),
        ("Azure AZ-104",                     "https://learn.microsoft.com/en-us/certifications/azure-administrator/"),
        ("Azure Administrator",              "https://learn.microsoft.com/en-us/certifications/azure-administrator/"),
        ("Google Cloud Digital Leader",      "https://cloud.google.com/learn/certification/cloud-digital-leader"),
        ("Tally Prime",                      "https://tallyeducation.com/"),
        ("Tally Education",                  "https://tallyeducation.com/"),
        // Govt / banking / SSC
        ("SSC CGL",                          "https://ssc.gov.in/"),
        ("IBPS PO",                          "https://www.ibps.in/"),
        ("IBPS Clerk",                       "https://www.ibps.in/"),
        ("RRB NTPC",                         "https://www.rrbcdg.gov.in/"),
        ("RRB",                              "https://www.rrbcdg.gov.in/"),
        ("UPSC CSE",                         "https://upsc.gov.in/"),
        ("UPSC",                             "https://upsc.gov.in/"),
        // Entrance — engineering / medical
        ("JEE Advanced",                     "https://jeeadv.ac.in/"),
        ("JEE Main",                         "https://jeemain.nta.nic.in/"),
        ("NEET UG",                          "https://neet.nta.nic.in/"),
        ("NEET",                             "https://neet.nta.nic.in/"),
        ("NDA",                              "https://upsc.gov.in/examinations/active-examinations/National-Defence-Academy-and-Naval-Academy-Examination"),
        // Entrance — commerce / arts / management
        ("CA Foundation",                    "https://www.icai.org/"),
        ("ICAI",                             "https://www.icai.org/"),
        ("CS Foundation",                    "https://www.icsi.edu/"),
        ("ICSI",                             "https://www.icsi.edu/"),
        ("CMA Foundation",                   "https://icmai.in/icmai/"),
        ("ICMAI",                            "https://icmai.in/icmai/"),
        ("CUET UG",                          "https://cuet.nta.nic.in/"),
        ("CUET",                             "https://cuet.nta.nic.in/"),
        ("CAT",                              "https://iimcat.ac.in/"),
        ("CLAT",                             "https://consortiumofnlus.ac.in/"),
        // Design / creative
        ("NID DAT",                          "https://www.nid.edu/admissions/"),
        ("NID",                              "https://www.nid.edu/admissions/"),
        ("NIFT",                             "https://www.nift.ac.in/admission"),
        // Skills / MOOC platforms
        ("NPTEL",                            "https://nptel.ac.in/"),
        ("Skill India Digital",              "https://www.skillindiadigital.gov.in/home"),
        ("Coursera",                         "https://www.coursera.org/"),
        ("freeCodeCamp",                     "https://www.freecodecamp.org/"),
        ("YouTube",                          "https://www.youtube.com/"),
        // Jobs / freelance
        ("Internshala",                      "https://internshala.com/"),
        ("Upwork",                           "https://www.upwork.com/"),
        ("LinkedIn Jobs",                    "https://www.linkedin.com/jobs/"),
        ("Naukri",                           "https://www.naukri.com/"),
    ];

    // Pre-sorted longest key first so the greedy scanner picks the most specific match.
    private static readonly (string KeyLower, string KeyOriginal, string Url)[] _sorted =
        _entries
            .Select(e => (KeyLower: e.Key.ToLowerInvariant(), KeyOriginal: e.Key, Url: e.Url))
            .OrderByDescending(e => e.KeyLower.Length)
            .ToArray();

    /// <summary>
    /// Returns the URL for the first known key found anywhere in <paramref name="title"/>.
    /// Use this to replace a Claude-invented resource URL with a verified one.
    /// </summary>
    public static string? LookupFirst(string title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        var lower = title.ToLowerInvariant();
        foreach (var (keyLower, _, url) in _sorted)
            if (lower.Contains(keyLower)) return url;
        return null;
    }

    /// <summary>
    /// Splits <paramref name="text"/> into segments. Segments with a non-null Url should
    /// be rendered as hyperlinks; segments with null Url are plain text.
    /// Longest match wins; overlapping matches are not possible.
    /// </summary>
    public static IReadOnlyList<(string Text, string? Url)> Segment(string text)
    {
        if (string.IsNullOrEmpty(text)) return [(text, null)];

        var result = new List<(string, string?)>();
        var lower = text.ToLowerInvariant();
        int pos = 0;

        while (pos < text.Length)
        {
            // Try to match any key starting exactly at pos (longest first).
            bool matched = false;
            foreach (var (keyLower, _, url) in _sorted)
            {
                if (pos + keyLower.Length > lower.Length) continue;
                if (string.Compare(lower, pos, keyLower, 0, keyLower.Length, StringComparison.Ordinal) != 0) continue;

                result.Add((text.Substring(pos, keyLower.Length), url));
                pos += keyLower.Length;
                matched = true;
                break;
            }

            if (!matched)
            {
                // Scan forward to find the earliest position where any key starts.
                int nextMatch = text.Length;
                foreach (var (keyLower, _, _) in _sorted)
                {
                    int idx = lower.IndexOf(keyLower, pos, StringComparison.Ordinal);
                    if (idx > pos && idx < nextMatch)
                        nextMatch = idx;
                }

                result.Add((text[pos..nextMatch], null));
                pos = nextMatch;
            }
        }

        return result;
    }
}
