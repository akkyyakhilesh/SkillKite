using Microsoft.EntityFrameworkCore;
using SkillKite.Core.Enums;
using SkillKite.Core.Models;

namespace SkillKite.Data.Seed;

/// <summary>
/// Seeds the 27 curated career paths from the SkillKite plan.
/// Idempotent — only inserts paths whose Title isn't already present.
/// Called from Program.cs at startup.
/// </summary>
public static class CareerPathSeed
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (await db.CareerPaths.AnyAsync(ct)) return;

        var paths = new List<CareerPath>
        {
            // --- Tech ---
            New("Full-Stack Web Developer", "फ़ुल-स्टैक वेब डेवलपर", CareerCategory.Tech,
                "Build modern web apps using .NET / MERN. High demand, remote-friendly.",
                "आधुनिक वेब ऐप बनाएँ .NET / MERN से। हाई डिमांड, रिमोट काम possible।",
                25_000, 80_000, true, DemandLevel.High, "6-9 months"),

            New("Mobile App Developer", "मोबाइल ऐप डेवलपर", CareerCategory.Tech,
                "Flutter or React Native — one codebase, Android + iOS.",
                "Flutter या React Native — एक कोड, Android और iOS दोनों।",
                25_000, 70_000, true, DemandLevel.High, "6-9 months"),

            New("Data Analyst", "डेटा एनालिस्ट", CareerCategory.Tech,
                "Excel + SQL + Python. Every company needs data people.",
                "Excel + SQL + Python। हर कंपनी को डेटा वाले चाहिए।",
                20_000, 60_000, true, DemandLevel.High, "4-6 months"),

            New("WordPress Developer + Freelancer", "WordPress डेवलपर + फ़्रीलांसर", CareerCategory.Tech,
                "Build websites for local businesses. Quick to monetize via Upwork/Fiverr.",
                "Local businesses के लिए वेबसाइट बनाएँ। Upwork/Fiverr से कमाई जल्दी।",
                15_000, 50_000, true, DemandLevel.Medium, "2-3 months"),

            New("UI/UX Designer", "UI/UX डिज़ाइनर", CareerCategory.Tech,
                "Figma + design fundamentals. Big startup demand.",
                "Figma और डिज़ाइन की समझ। स्टार्टअप्स में बहुत डिमांड।",
                20_000, 70_000, true, DemandLevel.Medium, "4-6 months"),

            New("Digital Marketing Specialist", "डिजिटल मार्केटिंग स्पेशलिस्ट", CareerCategory.Tech,
                "SEO, ads, social. Phone-friendly. Local + remote opportunities.",
                "SEO, ads, social media। फ़ोन से भी सीखा जा सकता है।",
                15_000, 50_000, true, DemandLevel.High, "3-5 months"),

            New("Cloud Computing (AWS/Azure)", "क्लाउड कंप्यूटिंग (AWS/Azure)", CareerCategory.Tech,
                "Foundational cloud skills — strong salary even at entry level.",
                "Cloud की बेसिक स्किल्स। शुरू में भी अच्छी सैलरी।",
                30_000, 90_000, true, DemandLevel.High, "6-9 months"),

            // --- Government ---
            New("SSC CGL Preparation", "SSC CGL तैयारी", CareerCategory.Government,
                "Central govt jobs — Income Tax Inspector, Excise, etc.",
                "केंद्र सरकार की नौकरी — Income Tax Inspector आदि।",
                25_000, 80_000, false, DemandLevel.High, "12-18 months"),

            New("Bank PO (IBPS)", "बैंक PO (IBPS)", CareerCategory.Government,
                "Public sector banking — stable, respected, pension.",
                "सरकारी बैंक — स्थायी, इज़्ज़त वाली, pension।",
                30_000, 70_000, false, DemandLevel.High, "12-18 months"),

            New("State PSC", "स्टेट PSC", CareerCategory.Government,
                "State-level civil services — Deputy Collector, DSP, etc.",
                "राज्य की सिविल सेवा — Deputy Collector, DSP आदि।",
                40_000, 1_00_000, false, DemandLevel.High, "18-24 months"),

            New("Railway (RRB)", "रेलवे (RRB)", CareerCategory.Government,
                "Indian Railways — Group D to NTPC, vast openings.",
                "भारतीय रेलवे — Group D से NTPC तक।",
                20_000, 50_000, false, DemandLevel.High, "9-12 months"),

            // --- Creative ---
            New("YouTube Content Creator", "YouTube कंटेंट क्रिएटर", CareerCategory.Creative,
                "Hindi education / vlogs. Phone is enough to start.",
                "Hindi में पढ़ाई या vlog। फ़ोन से शुरू कर सकते हैं।",
                10_000, 1_00_000, true, DemandLevel.Medium, "6-12 months"),

            New("Social Media Manager", "सोशल मीडिया मैनेजर", CareerCategory.Creative,
                "Run Instagram + YouTube for local businesses. Phone friendly.",
                "Local businesses के Instagram + YouTube चलाएँ।",
                15_000, 45_000, true, DemandLevel.High, "2-4 months"),

            New("Graphic Designer (Canva + Figma)", "ग्राफ़िक डिज़ाइनर", CareerCategory.Creative,
                "Posters, social posts, brand kits. Easy to freelance.",
                "Posters, social posts, brand kit। फ़्रीलांस आसान।",
                15_000, 40_000, true, DemandLevel.Medium, "3-5 months"),

            New("Freelance Content Writer", "फ़्रीलांस कंटेंट राइटर", CareerCategory.Creative,
                "Hindi + English content for blogs, brands, newsletters.",
                "Hindi/English blogs, brands, newsletters के लिए लिखें।",
                12_000, 50_000, true, DemandLevel.Medium, "2-4 months"),

            New("Video Editor", "वीडियो एडिटर", CareerCategory.Creative,
                "Edit reels, YouTube long-form. CapCut on phone, Premiere on laptop.",
                "Reels और YouTube videos edit करें।",
                15_000, 50_000, true, DemandLevel.High, "3-5 months"),

            // --- Gig / Remote ---
            New("Virtual Assistant", "वर्चुअल असिस्टेंट", CareerCategory.Gig,
                "Email, scheduling, research for foreign clients. Pays in USD.",
                "Foreign clients का email/research का काम। USD में payment।",
                15_000, 45_000, true, DemandLevel.Medium, "1-3 months"),

            New("Online Tutoring (Chegg/Vedantu)", "ऑनलाइन ट्यूटरिंग", CareerCategory.Gig,
                "Teach school/college subjects online. Hindi works.",
                "स्कूल/कॉलेज subjects ऑनलाइन पढ़ाएँ।",
                10_000, 40_000, true, DemandLevel.Medium, "1-2 months"),

            New("Freelance Transcription/Translation", "ट्रांसक्रिप्शन/अनुवाद", CareerCategory.Gig,
                "Hindi-English bilingual = unique advantage on Rev/Gengo.",
                "Hindi-English दोनों आती है = बड़ा फ़ायदा।",
                10_000, 30_000, true, DemandLevel.Low, "1-2 months"),

            New("E-commerce (Meesho / Shopify)", "ई-कॉमर्स", CareerCategory.Gig,
                "Reselling on Meesho or building a Shopify store.",
                "Meesho reselling या Shopify store।",
                10_000, 60_000, true, DemandLevel.Medium, "2-4 months"),

            // --- Trades ---
            New("Electrician + Solar Installation", "इलेक्ट्रीशियन + सोलर", CareerCategory.Trades,
                "ITI + solar certification. Massive govt push on solar.",
                "ITI + solar certification। सरकार solar पर बहुत ज़ोर दे रही है।",
                15_000, 40_000, false, DemandLevel.High, "6-9 months"),

            New("Mobile Phone Repair Technician", "मोबाइल रिपेयर टेक्नीशियन", CareerCategory.Trades,
                "3-month course → open a shop in your town.",
                "3 महीने का course → अपने शहर में दुकान।",
                12_000, 35_000, false, DemandLevel.Medium, "3 months"),

            New("Tally + GST Accountant", "Tally + GST अकाउंटेंट", CareerCategory.Trades,
                "Every small business needs one. Local job security.",
                "हर छोटे business को चाहिए। Local job पक्की।",
                12_000, 30_000, false, DemandLevel.High, "3-5 months"),

            New("Photography / Videography", "फ़ोटोग्राफ़ी / वीडियोग्राफ़ी", CareerCategory.Trades,
                "Weddings, events, reels — start with phone, upgrade later.",
                "शादी, events, reels। फ़ोन से शुरू, बाद में कैमरा।",
                15_000, 60_000, false, DemandLevel.Medium, "3-6 months"),

            // --- Emerging ---
            New("AI/ML Basics → Jr Data Scientist", "AI/ML बेसिक्स", CareerCategory.Emerging,
                "Python + math foundations. Hottest field, longer ramp.",
                "Python + maths। सबसे हॉट field, time लगेगा।",
                30_000, 1_00_000, true, DemandLevel.High, "9-18 months"),

            New("Cybersecurity Analyst", "साइबर सिक्योरिटी एनालिस्ट", CareerCategory.Emerging,
                "CompTIA + practical labs. Booming with India's data laws.",
                "CompTIA + labs। India में बहुत बढ़ रहा है।",
                25_000, 80_000, true, DemandLevel.High, "6-12 months"),

            New("Drone Pilot / Operator", "ड्रोन पायलट", CareerCategory.Emerging,
                "DGCA license. Agriculture, surveying, weddings.",
                "DGCA license। खेती, surveying, शादियाँ।",
                20_000, 60_000, false, DemandLevel.Medium, "3-6 months"),
        };

        db.CareerPaths.AddRange(paths);
        await db.SaveChangesAsync(ct);
    }

    private static CareerPath New(
        string title, string titleHi, CareerCategory category,
        string desc, string descHi,
        int salMin, int salMax, bool remote, DemandLevel demand, string timeToReady)
        => new()
        {
            Title = title,
            TitleHi = titleHi,
            Category = category,
            Description = desc,
            DescriptionHi = descHi,
            SalaryRangeMin = salMin,
            SalaryRangeMax = salMax,
            RemoteFriendly = remote,
            DemandLevel = demand,
            TimeToJobReady = timeToReady,
            RequirementsJson = "{}",
            IsActive = true
        };
}
