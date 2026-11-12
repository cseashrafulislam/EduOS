using EduOS.Core.Enums.Domain;

namespace EduOS.Service.Services.Tenants;

/// <summary>Versioned, reviewed defaults only. No prices, balances, dates, permissions or financial postings.</summary>
internal static class SmartSetupCatalog
{
    internal const string Version = "2026.11.1";
    internal sealed record Seed(string Module, string Kind, string Code, string Name,
        FeeFrequencyType? Frequency = null);
    internal sealed record Section(string Code, string Name, string Bangla, string Url, bool Apply, string Guidance);

    internal static readonly Section[] Sections =
    {
        new("ACADEMIC", "Academic references", "একাডেমিক মৌলিক তথ্য",
            "/AcademicManagement/Years", true, "Academic year, class, sections, curriculum, capacity and subject offerings require institution confirmation."),
        new("STUDENT", "Student administration", "শিক্ষার্থী", "/Students/Create", false,
            "Student and guardian status/relationship options are system-defined. Actual students are not seeded."),
        new("ADMISSION", "Admission", "ভর্তি", "/Admissions/Forms", false,
            "A published admission form, dates, program and fee policy require an administrator."),
        new("ATTENDANCE", "Attendance", "উপস্থিতি", "/AcademicRoutines/Index", false,
            "Attendance status codes are controlled by the application; configure schedules and calendar."),
        new("ASSESSMENT", "Exams and grading", "পরীক্ষা ও গ্রেড", "/AssessmentAdministration/GradeSchemes", false,
            "Grade thresholds and assessment plans are not assumed for a new institute."),
        new("FEES", "Fee heads", "ফি-এর ধরন", "/FeeOperations/Index", true,
            "Only fee-head names are prepared. Amounts, fee structures, receivables and ledger mapping remain unset."),
        new("FINANCE", "Accounting", "হিসাবরক্ষণ", "/AccountingSetup/Index", false,
            "Chart of accounts, bank accounts, tax, opening balances and postings require verified finance setup."),
        new("HR", "HR master data", "মানবসম্পদ", "/HrSetup/Index", true,
            "Designations and organization units only; no employees, salaries, contracts or leave balances."),
        new("PAYROLL", "Payroll setup", "বেতন ব্যবস্থাপনা", "/PayrollSalaryComponents/Index", false,
            "Salary component tax treatment, salary structures, pay rules and employee rates require administrator verification."),
        new("INVENTORY", "Inventory and asset categories", "ইনভেন্টরি ও সম্পদ", "/AssetLifecycle/Index", true,
            "Asset categories only; no stock, locations, inventory movement, items or opening valuation."),
        new("LIBRARY", "Library", "লাইব্রেরি", "/Library/Index", true,
            "Review borrowing and fine policies before enabling circulation."),
        new("HOSTEL", "Hostel", "আবাসন", "/HostelOperations/Setup", false,
            "Room/bed layout and pricing depend on actual campus facilities."),
        new("TRANSPORT", "Transport", "পরিবহন", "/TransportOperations/Setup", false,
            "Vehicles, routes and fares require actual operational data."),
        new("LMS", "Learning management", "অনলাইন শিক্ষা", "/Learning/Index", false,
            "Publish real courses, learning outcomes and content through the LMS workflow."),
        new("COMMUNICATION", "Communication", "যোগাযোগ", "/CommunicationAdministration/Index", true,
            "Review provider credentials, audience consent and message templates."),
        new("DOCUMENTS", "Documents", "নথি", "/DocumentAdministration/Index", true,
            "Private file policies and actual document evidence require review."),
        new("FACILITIES", "Facilities", "অবকাঠামো", "/FacilitySpaces/Index", false,
            "Real buildings, spaces and approval policies must be configured."),
        new("REPORTS", "Reports and MIS", "রিপোর্ট", "/CentralWorkCenter/Index", false,
            "Reports read authoritative transactions and must not create synthetic data.")
    };

    // UI setup groups are not always the same as licensed ProductModule codes.
    internal static string AccessModule(string sectionCode) => sectionCode switch
    {
        "FEES" => "FINANCE",
        "ASSESSMENT" => "EXAM",
        "REPORTS" => "REPORTING",
        _ => sectionCode
    };

    internal static IReadOnlyList<Seed> Build(string institutionTypeCode, string countryCode)
    {
        var type = (institutionTypeCode ?? "").Trim().ToUpperInvariant();
        var university = type.Contains("UNIVERSITY") || type.Contains("HIGHER");
        var college = !university && type.Contains("COLLEGE");
        var training = type.Contains("TRAIN") || type.Contains("TECHNICAL") ||
            type.Contains("VOCATIONAL") || type.Contains("POLYTECHNIC");
        var school = !university && !college && !training &&
            (type.Contains("SCHOOL") || type.Contains("MADRAS") || type.Contains("HYBRID"));
        var bangladesh = string.Equals(countryCode, "BD", StringComparison.OrdinalIgnoreCase);
        var list = new List<Seed>();
        // No Bangla medium outside Bangladesh unless a future reviewed locale preset enables it.
        if (bangladesh && (school || college))
            list.Add(new("ACADEMIC", "Medium", "BANGLA", "Bangla"));
        list.Add(new("ACADEMIC", "Medium", "ENGLISH", "English"));
        list.Add(new("HR", "OrganizationUnit", "ADMIN", "Administration"));
        list.Add(new("HR", "OrganizationUnit", "ACADEMIC", "Academics"));
        list.Add(new("HR", "Designation", "OFFICE-STAFF", "Office Staff"));
        list.Add(new("HR", "Designation", university || college ? "LECTURER" :
            training ? "INSTRUCTOR" : "TEACHER", university || college ? "Lecturer" :
            training ? "Instructor" : "Teacher"));
        // Fee head is a label, not an actual fee/ledger. Do not infer schedules or amounts.
        list.Add(new("FEES", "FeeHead", "TUITION", "Tuition Fee", FeeFrequencyType.Monthly));
        list.Add(new("FEES", "FeeHead", "ADMISSION", "Admission Fee", FeeFrequencyType.OneTime));
        list.Add(new("INVENTORY", "AssetCategory", "FURNITURE", "Furniture"));
        list.Add(new("INVENTORY", "AssetCategory", "EQUIPMENT", "Equipment"));
        list.Add(new("LIBRARY", "BookCategory", "TEXTBOOK", "Textbook"));
        list.Add(new("LIBRARY", "BookCategory", "REFERENCE", "Reference"));
        list.Add(new("LIBRARY", "BookCategory", "FICTION", "Fiction"));
        list.Add(new("COMMUNICATION", "NoticeCategory", "GENERAL", "General Notice"));
        list.Add(new("COMMUNICATION", "NoticeCategory", "ACADEMIC", "Academic Notice"));
        list.Add(new("DOCUMENTS", "DocumentType", "GENERAL", "General Document"));
        return list;
    }
}
