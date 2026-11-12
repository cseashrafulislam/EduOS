using EduOS.Core.Enums.Domain;

namespace EduOS.Service.Services.Tenants;

/// <summary>Versioned, reviewed defaults only. No prices, balances, dates, permissions or financial postings.</summary>
internal static class SmartSetupCatalog
{
    internal const string Version = "2026.11.3";
    internal sealed record Seed(string Module, string Kind, string Code, string Name,
        FeeFrequencyType? Frequency = null, AccountType? AccountType = null,
        bool IsGroup = false, string? ParentCode = null, SalaryComponentType? SalaryType = null,
        bool IsTaxable = false);
    internal sealed record GradeBand(decimal Min, decimal Max, string Letter, decimal Point);
    internal static readonly IReadOnlyDictionary<string, GradeBand[]> GradePresets =
        new Dictionary<string, GradeBand[]>(StringComparer.Ordinal)
        {
            ["BD-GPA-5"] =
            [
                new(0m, 32.99m, "F", 0m), new(33m, 39.99m, "D", 1m),
                new(40m, 49.99m, "C", 2m), new(50m, 59.99m, "B", 3m),
                new(60m, 69.99m, "A-", 3.50m), new(70m, 79.99m, "A", 4m),
                new(80m, 100m, "A+", 5m)
            ],
            ["BD-CGPA-4"] =
            [
                new(0m, 39.99m, "F", 0m), new(40m, 44.99m, "D", 2m),
                new(45m, 49.99m, "C", 2.25m), new(50m, 54.99m, "C+", 2.50m),
                new(55m, 59.99m, "B-", 2.75m), new(60m, 64.99m, "B", 3m),
                new(65m, 69.99m, "B+", 3.25m), new(70m, 74.99m, "A-", 3.50m),
                new(75m, 79.99m, "A", 3.75m), new(80m, 100m, "A+", 4m)
            ]
        };
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
        new("ASSESSMENT", "Exams and grading", "পরীক্ষা ও গ্রেড", "/AssessmentAdministration/GradeSchemes", true,
            "Bangladesh Board GPA 5.00 and UGC-style CGPA 4.00 templates are available for review. Neither is made default or assigned to a programme."),
        new("FEES", "Fee heads", "ফি-এর ধরন", "/FeeOperations/Index", true,
            "Only fee-head names are prepared. Amounts, fee structures, receivables and ledger mapping remain unset."),
        new("FINANCE", "Accounting", "হিসাবরক্ষণ", "/AccountingSetup/Index", true,
            "Creates only reviewed BDT ledger groups and basic posting-account labels. No cash designation, banking, opening balances, postings or tax rules."),
        new("HR", "HR master data", "মানবসম্পদ", "/HrSetup/Index", true,
            "Designations and organization units only; no employees, salaries, contracts or leave balances."),
        new("PAYROLL", "Payroll setup", "বেতন ব্যবস্থাপনা", "/PayrollSalaryComponents/Index", true,
            "Creates only reviewed salary component names and types. Tax treatment must be checked before payroll; no amounts, employees or salary structures."),
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
        "PAYROLL" => "HR",
        "REPORTS" => "REPORTING",
        _ => sectionCode
    };

    // Non-operational, tenant-scoped reference catalog. These are choices, not actual
    // employees, programmes, dates, rooms, bank accounts, fee amounts or transactions.
    // Each setting persists in its EXISTING canonical master table.
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
        static void Add(List<Seed> target, string module, string kind,
            params (string Code, string Name)[] entries)
        {
            foreach (var (code, name) in entries)
                target.Add(new(module, kind, code, name));
        }
        // Academic medium is deliberately 1–2 real options, not 12 fabricated languages.
        if (bangladesh && (school || college))
            list.Add(new("ACADEMIC", "Medium", "BANGLA", "Bangla"));
        list.Add(new("ACADEMIC", "Medium", "ENGLISH", "English"));


        // Subject master only: no curricula, subject offerings, class allocations or
        // credit/full/pass marks are inferred. Broad BD NCTB/board labels are reviewed.
        if (bangladesh && !university && !training)
        {
            Add(list, "ACADEMIC", "Subject",
                ("BANGLA", "Bangla"), ("BANGLA-1", "Bangla First Paper"),
                ("BANGLA-2", "Bangla Second Paper"), ("ENGLISH", "English"),
                ("ENGLISH-1", "English First Paper"), ("ENGLISH-2", "English Second Paper"),
                ("MATH", "Mathematics"), ("HIGHER-MATH", "Higher Mathematics"),
                ("GENERAL-SCIENCE", "General Science"), ("BGS", "Bangladesh and Global Studies"),
                ("ICT", "Information and Communication Technology"),
                ("PHYSICS", "Physics"), ("CHEMISTRY", "Chemistry"), ("BIOLOGY", "Biology"),
                ("AGRICULTURE", "Agricultural Studies"), ("PHYSICAL-EDUCATION", "Physical Education and Health"),
                ("CAREER-EDUCATION", "Career Education"), ("ARTS-CRAFTS", "Arts and Crafts"),
                ("ISLAM-STUDIES", "Islam and Moral Education"), ("HINDU-STUDIES", "Hindu Religion and Moral Education"),
                ("BUDDHIST-STUDIES", "Buddhist Religion and Moral Education"),
                ("CHRISTIAN-STUDIES", "Christian Religion and Moral Education"),
                ("ACCOUNTING", "Accounting"), ("FINANCE-BANKING", "Finance, Banking and Insurance"),
                ("BUSINESS-ORGANIZATION", "Business Organization and Management"),
                ("ECONOMICS", "Economics"), ("STATISTICS", "Statistics"),
                ("GEOGRAPHY", "Geography"), ("CIVICS", "Civics and Good Governance"),
                ("HISTORY", "History"), ("ISLAMIC-HISTORY", "Islamic History and Culture"),
                ("SOCIOLOGY", "Sociology"), ("PSYCHOLOGY", "Psychology"),
                ("LOGIC", "Logic"), ("HOME-SCIENCE", "Home Science"));
            if (type.Contains("MADRAS"))
                Add(list, "ACADEMIC", "Subject",
                    ("ARABIC", "Arabic"), ("QURAN", "Quran Majid and Tajwid"),
                    ("HADITH", "Hadith Sharif"), ("FIQH", "Fiqh and Aqidah"));
        }
        else if (bangladesh && university)
            Add(list, "ACADEMIC", "Subject",
                ("BANGLA", "Bangla"), ("ENGLISH", "English"),
                ("ICT", "Information and Communication Technology"),
                ("STATISTICS", "Statistics"), ("ECONOMICS", "Economics"),
                ("BUSINESS-ORGANIZATION", "Business Organization and Management"),
                ("ACCOUNTING", "Accounting"), ("MATHEMATICS", "Mathematics"),
                ("COMPUTER-SCIENCE", "Computer Science"), ("PHYSICS", "Physics"),
                ("CHEMISTRY", "Chemistry"), ("BIOLOGY", "Biology"));
        else if (bangladesh && training)
            Add(list, "ACADEMIC", "Subject",
                ("BANGLA", "Bangla"), ("ENGLISH", "English"),
                ("MATHEMATICS", "Mathematics"), ("ICT", "Information and Communication Technology"),
                ("PHYSICS", "Physics"), ("CHEMISTRY", "Chemistry"),
                ("ENGINEERING-DRAWING", "Engineering Drawing"));
        if (bangladesh)
        {
            list.Add(new("ASSESSMENT", "GradeScheme", "BD-GPA-5", "Bangladesh Board GPA 5.00"));
            list.Add(new("ASSESSMENT", "GradeScheme", "BD-CGPA-4", "Bangladesh UGC-style CGPA 4.00"));
        }

        if (bangladesh)
        {
            // Canonical account groups and general posting ledgers, but no real bank,
            // cash designation, receivable/debtor mapping, balances or journal entries.
            foreach (var (code, name, type) in new (string, string, AccountType)[]
            {
                ("1000", "Assets", AccountType.Asset),
                ("2000", "Liabilities", AccountType.Liability),
                ("3000", "Equity", AccountType.Equity),
                ("4000", "Income", AccountType.Income),
                ("5000", "Expenses", AccountType.Expense)
            })
                list.Add(new("FINANCE", "Account", code, name, AccountType: type, IsGroup: true));
            foreach (var (code, name, type, parent) in new (string, string, AccountType, string)[]
            {
                ("1100", "Accounts Receivable", AccountType.Asset, "1000"),
                ("1200", "Furniture and Equipment", AccountType.Asset, "1000"),
                ("2100", "Accounts Payable", AccountType.Liability, "2000"),
                ("2200", "Salaries Payable", AccountType.Liability, "2000"),
                ("3100", "Capital and Contributions", AccountType.Equity, "3000"),
                ("4100", "Tuition Fee Income", AccountType.Income, "4000"),
                ("4200", "Admission Fee Income", AccountType.Income, "4000"),
                ("5100", "Salary Expense", AccountType.Expense, "5000"),
                ("5200", "Utilities Expense", AccountType.Expense, "5000"),
                ("5300", "Office Expense", AccountType.Expense, "5000")
            })
                list.Add(new("FINANCE", "Account", code, name, AccountType: type, ParentCode: parent));

            // Taxability is a reviewed, editable master policy: the conservative
            // earnings default is taxable; deductions and employer costs are not.
            // This creates no compensation, withholding, statutory rate or posting.
            foreach (var (code, name, kind) in new (string, string, SalaryComponentType)[]
            {
                ("BASIC", "Basic Salary", SalaryComponentType.Earning),
                ("HOUSE-RENT", "House Rent Allowance", SalaryComponentType.Earning),
                ("MEDICAL-ALLOWANCE", "Medical Allowance", SalaryComponentType.Earning),
                ("TRANSPORT-ALLOWANCE", "Transport Allowance", SalaryComponentType.Earning),
                ("OVERTIME", "Overtime", SalaryComponentType.Earning),
                ("FESTIVAL-BONUS", "Festival Bonus", SalaryComponentType.Earning),
                ("ABSENCE-DEDUCTION", "Absence Deduction", SalaryComponentType.Deduction),
                ("LOAN-RECOVERY", "Loan Recovery", SalaryComponentType.Deduction),
                ("EMPLOYEE-PF", "Employee Provident Fund Deduction", SalaryComponentType.Deduction),
                ("EMPLOYER-PF", "Employer Provident Fund Contribution", SalaryComponentType.EmployerContribution)
            })
                list.Add(new("PAYROLL", "SalaryComponent", code, name,
                    SalaryType: kind, IsTaxable: kind == SalaryComponentType.Earning));

        }
        Add(list, "HR", "OrganizationUnit",
            ("ADMIN", "Administration"),
            ("ACADEMIC", "Academics"),
            ("ACCOUNTS", "Accounts and Finance"),
            ("HR", "Human Resources"),
            ("ADMISSION", "Admissions"),
            ("EXAMINATION", "Examination"),
            ("LIBRARY", "Library"),
            ("IT", "IT Support"),
            ("STUDENT-AFFAIRS", "Student Affairs"),
            ("MAINTENANCE", "Maintenance"),
            ("OPERATIONS", "Operations"),
            ("PROCUREMENT", "Procurement"));
        Add(list, "HR", "Designation",
            ("OFFICE-STAFF", "Office Staff"),
            (university || college ? "LECTURER" : training ? "INSTRUCTOR" : "TEACHER",
                university || college ? "Lecturer" : training ? "Instructor" : "Teacher"),
            ("ACCOUNTANT", "Accountant"),
            ("LIBRARIAN", "Librarian"),
            ("ADMIN-OFFICER", "Administrative Officer"),
            ("IT-OFFICER", "IT Officer"),
            ("ADMISSION-OFFICER", "Admission Officer"),
            ("EXAM-OFFICER", "Examination Officer"),
            ("RECEPTIONIST", "Receptionist"),
            ("OFFICE-ASSISTANT", "Office Assistant"),
            ("SECURITY-OFFICER", "Security Officer"),
            ("MAINTENANCE-STAFF", "Maintenance Staff"));
        // Fee heads are labels only. No amount, student bill, bank/GL reference,
        // income recognition or cash transaction may be inferred by the catalog.
        foreach (var (code, name, frequency) in new (string, string, FeeFrequencyType)[]
        {
            ("TUITION", "Tuition Fee", FeeFrequencyType.Monthly),
            ("ADMISSION", "Admission Fee", FeeFrequencyType.OneTime),
            ("REGISTRATION", "Registration Fee", FeeFrequencyType.OneTime),
            ("EXAMINATION", "Examination Fee", FeeFrequencyType.PerTerm),
            ("LIBRARY", "Library Fee", FeeFrequencyType.Yearly),
            ("ID-CARD", "Identity Card Fee", FeeFrequencyType.OneTime),
            ("LABORATORY", "Laboratory Fee", FeeFrequencyType.PerTerm),
            ("SPORTS", "Sports Fee", FeeFrequencyType.Yearly),
            ("TRANSPORT", "Transport Fee", FeeFrequencyType.Monthly),
            ("HOSTEL", "Hostel Fee", FeeFrequencyType.Monthly),
            ("CERTIFICATE", "Certificate Fee", FeeFrequencyType.OneTime),
            ("TECHNOLOGY", "Technology Fee", FeeFrequencyType.Yearly)
        })
            list.Add(new("FEES", "FeeHead", code, name, frequency));

        Add(list, "INVENTORY", "AssetCategory",
            ("FURNITURE", "Furniture"),
            ("EQUIPMENT", "Equipment"),
            ("COMPUTERS", "Computers"),
            ("NETWORKING", "Networking"),
            ("ELECTRICAL", "Electrical Equipment"),
            ("LABORATORY", "Laboratory Equipment"),
            ("LIBRARY", "Library Fixtures"),
            ("VEHICLES", "Vehicles"),
            ("APPLIANCES", "Office Appliances"),
            ("CLASSROOM", "Classroom Fixtures"),
            ("SAFETY", "Safety Equipment"),
            ("SPORTS", "Sports Equipment"));

        Add(list, "LIBRARY", "BookCategory",
            ("TEXTBOOK", "Textbook"),
            ("REFERENCE", "Reference"),
            ("FICTION", "Fiction"),
            ("NONFICTION", "Non-fiction"),
            ("PERIODICAL", "Periodical"),
            ("RESEARCH", "Research"),
            ("DIGITAL", "Digital"),
            ("CHILDREN", "Children"),
            ("LITERATURE", "Literature"),
            ("LANGUAGE", "Language Learning"),
            ("CAREER", "Career Guidance"),
            ("EXAM-PREP", "Exam Preparation"));

        Add(list, "COMMUNICATION", "NoticeCategory",
            ("GENERAL", "General Notice"),
            ("ACADEMIC", "Academic Notice"),
            ("EXAMINATION", "Examination Notice"),
            ("ADMISSION", "Admission Notice"),
            ("FEES", "Fee Notice"),
            ("ATTENDANCE", "Attendance Notice"),
            ("HOLIDAY", "Holiday Notice"),
            ("EVENT", "Event Notice"),
            ("LIBRARY", "Library Notice"),
            ("EMERGENCY", "Emergency Notice"),
            ("GUARDIAN", "Guardian Notice"),
            ("STAFF", "Staff Notice"));

        // These are ordinary document categories, not sensitive document content
        // or verification/retention policies; no private FileAsset is invented.
        Add(list, "DOCUMENTS", "DocumentType",
            ("GENERAL", "General Document"),
            ("CIRCULAR", "Circular"),
            ("NOTICE", "Notice"),
            ("POLICY", "Policy"),
            ("GUIDE", "User Guide"),
            ("MINUTES", "Meeting Minutes"),
            ("TEMPLATE", "Template"),
            ("CHECKLIST", "Checklist"),
            ("REPORT", "Report"),
            ("PROPOSAL", "Proposal"),
            ("SCHEDULE", "Schedule"),
            ("PROCEDURE", "Procedure"));
        return list;
    }
}
