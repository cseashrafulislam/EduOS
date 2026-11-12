namespace EduOS.Service.Services.Academic;

/// <summary>
/// Reviewed Ministry of Public Administration (MoPA) gazette dates. This is a
/// versioned snapshot, not a live ministry API. Optional/religion-specific
/// and region-limited leaves are NOT automatic institution-wide closures.
/// Amendments and moon-dependent dates require administrator review.
/// </summary>
internal static class BangladeshGovernmentHolidayCatalog
{
    internal const string Version = "BD-MOPA-2026-2027-v2";
    internal const string Source2026 = "https://mopa.gov.bd/pages/public-holiday?archived=true";
    internal const string Source2027 = "https://mopa.gov.bd/pages/public-holiday";
    internal sealed record Holiday(string Code, string Name, DateOnly Start, DateOnly End,
        bool MoonDependent, int GazetteYear, string Source, string Note = "");

    private static Holiday Day(int year, int month, int day, string code, string title,
        bool lunar = false, string note = "") =>
        new(code, title, new DateOnly(year, month, day), new DateOnly(year, month, day),
            lunar, year, year == 2026 ? Source2026 : Source2027, note);

    private static Holiday Period(int year, int month, int first, int last,
        string code, string title, bool lunar) =>
        new(code, title, new DateOnly(year, month, first), new DateOnly(year, month, last),
            lunar, year, year == 2026 ? Source2026 : Source2027,
            "Combined general and executive-order dates from the gazette.");

    internal static readonly IReadOnlyList<Holiday> Entries = new List<Holiday>
    {
        // MoPA notification of 9 November 2025, 2026 general + executive holidays.
        Day(2026, 2, 3, "BD26-SHAB-BARAT", "Shab-e-Barat", true),
        Day(2026, 2, 21, "BD26-LANGUAGE", "Language Martyrs' Day"),
        Day(2026, 3, 17, "BD26-SHAB-QADR", "Shab-e-Qadr", true),
        Period(2026, 3, 19, 23, "BD26-EID-FITR", "Eid-ul-Fitr (government closure)", true),
        Day(2026, 3, 20, "BD26-JUMATUL-BIDA", "Jumatul Bida", true),
        Day(2026, 3, 26, "BD26-INDEPENDENCE", "Independence and National Day"),
        Day(2026, 4, 14, "BD26-BENGALI-NEW-YEAR", "Bengali New Year"),
        Day(2026, 5, 1, "BD26-MAY-DAY", "May Day"),
        Day(2026, 5, 1, "BD26-BUDDHA-PURNIMA", "Buddha Purnima", true),
        Period(2026, 5, 26, 31, "BD26-EID-ADHA", "Eid-ul-Adha (government closure)", true),
        Day(2026, 6, 26, "BD26-ASHURA", "Ashura", true),
        Day(2026, 8, 5, "BD26-JULY-UPRISING", "July Mass Uprising Day"),
        Day(2026, 8, 26, "BD26-EID-MILAD", "Eid-e-Miladunnabi", true),
        Day(2026, 9, 4, "BD26-JANMASHTAMI", "Janmashtami"),
        Day(2026, 10, 20, "BD26-DURGA-NAVAMI", "Durga Puja (Navami)"),
        Day(2026, 10, 21, "BD26-DURGA-DASHAMI", "Durga Puja (Vijaya Dashami)"),
        new Holiday("BD26-REVOLUTION", "National Revolution and Solidarity Day",
            new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 7), false, 2026,
            "https://mopa.gov.bd/pages/go-ultimates/6ac5e76d9594596dad446b72",
            "Additional MoPA public holiday notification dated 7 October 2026."),
        Day(2026, 12, 16, "BD26-VICTORY", "Victory Day"),
        Day(2026, 12, 25, "BD26-CHRISTMAS", "Christmas Day"),

        // MoPA notification of 5 November 2026, 2027 general + executive holidays.
        Day(2027, 1, 24, "BD27-SHAB-BARAT", "Shab-e-Barat", true),
        Day(2027, 2, 21, "BD27-LANGUAGE", "Language Martyrs' Day"),
        Day(2027, 3, 5, "BD27-JUMATUL-BIDA", "Jumatul Bida", true),
        Day(2027, 3, 7, "BD27-SHAB-QADR", "Shab-e-Qadr", true),
        Period(2027, 3, 8, 12, "BD27-EID-FITR", "Eid-ul-Fitr (government closure)", true),
        Day(2027, 3, 26, "BD27-INDEPENDENCE", "Independence and National Day"),
        Day(2027, 4, 14, "BD27-BENGALI-NEW-YEAR", "Bengali New Year"),
        Day(2027, 5, 1, "BD27-MAY-DAY", "May Day"),
        Period(2027, 5, 15, 20, "BD27-EID-ADHA", "Eid-ul-Adha (government closure)", true),
        Day(2027, 5, 20, "BD27-BUDDHA-PURNIMA", "Buddha Purnima", true),
        Day(2027, 6, 16, "BD27-ASHURA", "Ashura", true),
        Day(2027, 8, 5, "BD27-JULY-UPRISING", "July Mass Uprising Day"),
        Day(2027, 8, 15, "BD27-EID-MILAD", "Eid-e-Miladunnabi", true),
        Day(2027, 8, 25, "BD27-JANMASHTAMI", "Janmashtami"),
        Day(2027, 10, 9, "BD27-DURGA-NAVAMI", "Durga Puja (Navami)"),
        Day(2027, 10, 10, "BD27-DURGA-DASHAMI", "Durga Puja (Vijaya Dashami)"),
        Day(2027, 11, 7, "BD27-REVOLUTION", "National Revolution and Solidarity Day"),
        Day(2027, 12, 16, "BD27-VICTORY", "Victory Day"),
        Day(2027, 12, 25, "BD27-CHRISTMAS", "Christmas Day")
    };

    internal static IReadOnlyList<Holiday> InRange(DateOnly from, DateOnly to) =>
        Entries.Where(x => x.Start >= from && x.End <= to)
            .OrderBy(x => x.Start).ThenBy(x => x.Code).ToArray();
}
