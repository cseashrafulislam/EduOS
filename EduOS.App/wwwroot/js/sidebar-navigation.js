/* Progressive enhancement for the server-authorized EduOS navigation.
 * All destinations come from Razor's role and module-entitlement checks.
 * This script only groups links already rendered for the current user. */
(() => {
    'use strict';

    const groups = {
        platform: [
            ['institutions', 'Institutions', 'প্রতিষ্ঠান', ['/PlatformTenants', '/PlatformCatalogAdministration']],
            ['features', 'Products & Features', 'প্রোডাক্ট ও ফিচার', ['/PlatformFeatureAdministration', '/PlatformProductModuleAdministration', '/PlatformInstitutionModulePresets', '/PlatformModuleFeatures']],
            ['subscriptions', 'Plans & Payments', 'প্ল্যান ও পেমেন্ট', ['/SubscriptionPlanAdministration', '/SubscriptionPaymentHistory', '/Account/PaymentVerification']],
            ['governance', 'Platform Governance', 'প্ল্যাটফর্ম পরিচালনা', ['/LegalDocuments']]
        ],
        setup: [
            ['access', 'Users & Access', 'ব্যবহারকারী ও অনুমতি', ['/TenantUsers', '/TenantPermissionOverrides']],
            ['institution', 'Institution & Settings', 'প্রতিষ্ঠান ও সেটিংস', ['/InstitutionManagement', '/TenantDomains', '/TenantTerminology', '/TenantSettings', '/NumberSeries', '/TenantModules', '/SubscriptionBilling', '/IntegrationAdministration']],
            ['enrollment', 'Student Administration', 'শিক্ষার্থী ব্যবস্থাপনা', ['/StudentLifecycle', '/StudentDocuments', '/StudentGuardians', '/StudentTransferRequests', '/EnrollmentManagement', '/StudentImports', '/StudentReadmissions', '/StudentProgressions']],
            ['academic-setup', 'Academic Management', 'একাডেমিক ব্যবস্থাপনা', ['/AcademicManagement']]
        ],
        academics: [
            ['portal', 'My Learning Portal', 'আমার লার্নিং পোর্টাল', ['/StudentPortal', '/StudentSubjects', '/Alumni']],
            ['learners', 'Admissions & Students', 'ভর্তি ও শিক্ষার্থী', ['/Admissions', '/Students', '/LearnerIdentities']],
            ['career', 'Career & Placement', 'ক্যারিয়ার ও কর্মসংস্থান', ['/CareerEmployers', '/CareerOpportunities', '/CareerPlacements']],
            ['research', 'Research & Ethics', 'গবেষণা ও নৈতিক অনুমোদন', ['/ResearchProposals', '/ResearchGrants']],
            ['accreditation', 'Accreditation & Quality', 'স্বীকৃতি ও মান', ['/Accreditation']],
            ['learning', 'Learning & Courses', 'শিক্ষা ও কোর্স', ['/Learning']],
            ['assessments', 'Exams & Results', 'পরীক্ষা ও ফলাফল', ['/AssessmentAdministration']],
            ['operations', 'Classes & Attendance', 'ক্লাস ও উপস্থিতি', ['/AcademicRoutines', '/AcademicInstruction', '/AcademicCalendar', '/StudentLeaves', '/Attendance']]
        ],
        management: [
            ['people', 'HR & Payroll', 'এইচআর ও বেতন', ['/HR', '/HrEmployeeRecords', '/EmployeeContracts', '/Recruitment', '/EmployeeAttendance', '/LeaveEntitlements', '/PayrollOperations', '/PayrollSalaryStructures', '/PayrollSalaryComponents', '/PayrollSalaryStatement', '/PayrollReconciliation', '/PayrollBonuses', '/PayrollJournalPosting', '/PayrollPaymentPosting', '/PayrollLoanRegister', '/LoanDisbursement', '/LoanRecovery', '/EmployeePortal']],
            ['finance', 'Fees & Accounting', 'ফি ও হিসাব', ['/FeeOperations', '/FeeDiscountRules', '/FeeReceivablesAging', '/FeeHeadAccounting', '/AdmissionCashReceipts', '/FinanceAdjustments', '/AccountingSetup', '/AccountingJournals', '/AccountingReports', '/StudentInvoicePosting', '/StudentPaymentPosting', '/FeeDueCalendar', '/FeeInstallmentProjection', '/FeeInstallmentPlans', '/FeeInvoiceCoverage', '/FeeDiscountAttributionReview']],
            ['inventory', 'Inventory & Assets', 'ইনভেন্টরি ও সম্পদ', ['/InventoryCatalog', '/InventoryMovements', '/AssetLifecycle']],
            ['campus', 'Campus Operations', 'ক্যাম্পাস পরিচালনা', ['/HostelOperations', '/hostel', '/TransportOperations', '/OperationsInsights', '/FacilityWorkOrders', '/FacilitySpaces']],
            ['library', 'Library', 'গ্রন্থাগার', ['/Library']],
            ['communication', 'Communication & Documents', 'যোগাযোগ ও ডকুমেন্ট', ['/CommunicationWorkspace', '/CommunicationAdministration', '/CustomFields', '/DocumentAdministration']],
            ['governance', 'Audit & Reporting', 'অডিট ও রিপোর্ট', ['/AuditLogs', '/ImportHistory']]
        ]
    };

    // Optional third level: Module > Settings/Operations/Reports > feature.
    // Only categorize links that Razor has already authorized and emitted.
    const categories = {
        'setup:academic-setup': [
            ['settings', 'Settings', 'সেটিংস', ['/AcademicManagement']],
            ['operations', 'Operations', 'কার্যক্রম', ['/AcademicManagement/Offerings']]
        ],
        'setup:institution': [
            ['settings', 'Settings', 'সেটিংস', ['/InstitutionManagement/Profile', '/InstitutionManagement/Regional', '/InstitutionManagement/Branding', '/InstitutionManagement/Subdomain', '/InstitutionManagement/Index', '/TenantDomains', '/TenantTerminology', '/TenantSettings', '/NumberSeries', '/TenantModules']],
            ['operations', 'Operations', 'কার্যক্রম', ['/InstitutionManagement/Campuses', '/SubscriptionBilling', '/IntegrationAdministration']],
            ['reports', 'Reports', 'রিপোর্ট', ['/InstitutionManagement/History']]
        ],
        'setup:enrollment': [
            ['settings', 'Settings', 'সেটিংস', ['/StudentGuardians', '/StudentDocuments']],
            ['operations', 'Operations', 'কার্যক্রম', ['/EnrollmentManagement', '/StudentImports', '/StudentReadmissions', '/StudentProgressions', '/StudentLifecycle', '/StudentTransferRequests']]
        ],
        'academics:learners': [
            ['settings', 'Settings', 'সেটিংস', ['/Admissions/Forms', '/Admissions/AssessmentSetup']],
            ['operations', 'Operations', 'কার্যক্রম', ['/Admissions/NewApplication', '/Admissions/Applications', '/Admissions/Merit', '/Admissions/Assessments', '/Admissions/Offers', '/Admissions/Index', '/Students', '/LearnerIdentities/Index']],
            ['reports', 'Reports', 'রিপোর্ট', ['/LearnerIdentities/AccessHistory']]
        ],
        'academics:career': [
            ['settings', 'Settings', 'সেটিংস', ['/CareerEmployers']],
            ['operations', 'Operations', 'কার্যক্রম', ['/CareerOpportunities', '/CareerPlacements']]
        ],
        'academics:research': [
            ['operations', 'Operations', 'কার্যক্রম', ['/ResearchProposals', '/ResearchGrants']]
        ],
        'academics:accreditation': [
            ['operations', 'Operations & Reports', 'কার্যক্রম ও রিপোর্ট', ['/Accreditation']]
        ],
        'academics:learning': [
            ['settings', 'Settings', 'সেটিংস', ['/Learning/CourseContent']],
            ['operations', 'Operations', 'কার্যক্রম', ['/Learning/Index', '/Learning/Quizzes', '/Learning/LiveClasses', '/Learning/AssignmentFiles', '/Learning/MySubmissions']],
            ['reports', 'Reports', 'রিপোর্ট', ['/Learning/CourseCompletions', '/Learning/Certificates']]
        ],
        'management:finance': [
            ['settings', 'Settings', 'সেটিংস', ['/FeeOperations/Structures', '/FeeDiscountRules', '/FeeHeadAccounting', '/AccountingSetup', '/FinanceAdjustments/Policies']],
            ['operations', 'Operations', 'কার্যক্রম', ['/FeeOperations/Billing', '/FeeOperations/Ledger', '/FeeOperations', '/AdmissionCashReceipts', '/FinanceAdjustments/StudentAdjustments', '/FinanceAdjustments/Refunds', '/FinanceAdjustments/HostelClearances', '/FinanceAdjustments/TransportClearances', '/FinanceAdjustments/ReservationPreflight', '/FinanceAdjustments/DiscountAwards', '/FinanceAdjustments', '/AccountingJournals', '/StudentInvoicePosting', '/StudentPaymentPosting', '/FeeInstallmentPlans']],
            ['reports', 'Reports', 'রিপোর্ট', ['/FeeReceivablesAging', '/AccountingReports', '/FinanceAdjustments/FineRegister', '/FeeDueCalendar', '/FeeInstallmentProjection', '/FeeInvoiceCoverage', '/FeeDiscountAttributionReview']]
        ],
        'academics:assessments': [
            ['settings', 'Settings', 'সেটিংস', ['/AssessmentAdministration/Setup', '/AssessmentAdministration/AssessmentComponents', '/AssessmentAdministration/AssessmentSchedules', '/AssessmentAdministration/GradeSchemes', '/AssessmentAdministration/CertificateTemplates']],
            ['operations', 'Operations', 'কার্যক্রম', ['/AssessmentAdministration/Marks', '/AssessmentAdministration/Results', '/AssessmentAdministration/Index', '/AssessmentAdministration/MarkReviews', '/AssessmentAdministration/ComponentMarks', '/AssessmentAdministration/SubjectCompletions', '/AssessmentAdministration/Attempts', '/AssessmentAdministration/ResultRescrutiny', '/AssessmentAdministration/AdmitCards']],
            ['reports', 'Reports', 'রিপোর্ট', ['/AssessmentAdministration/ExamReports', '/AssessmentAdministration/Publications', '/AssessmentAdministration/GradeDistribution', '/AssessmentAdministration/ResultFollowUp', '/AssessmentAdministration/Certificates', '/AssessmentAdministration/Transcripts']]
        ],
        'academics:operations': [
            ['settings', 'Settings', 'সেটিংস', ['/AcademicCalendar']],
            ['operations', 'Operations', 'কার্যক্রম', ['/AcademicRoutines', '/AcademicInstruction', '/StudentLeaves', '/Attendance/Register', '/Attendance/Index']],
            ['reports', 'Reports', 'রিপোর্ট', ['/Attendance/Reports', '/Attendance/Corrections']]
        ],
        'management:people': [
            ['settings', 'Settings', 'সেটিংস', ['/HR/ShiftAssignments', '/HR/CampusAssignments', '/HR/BankAccounts', '/HR', '/HrEmployeeRecords', '/LeaveEntitlements', '/EmployeeContracts', '/Recruitment', '/PayrollSalaryStructures', '/PayrollSalaryComponents']],
            ['operations', 'Operations', 'কার্যক্রম', ['/HR/Directory', '/HR/Leaves', '/EmployeeAttendance', '/PayrollOperations', '/PayrollBonuses', '/PayrollLoanRegister', '/LoanDisbursement', '/LoanRecovery', '/PayrollJournalPosting', '/PayrollPaymentPosting', '/EmployeePortal']],
            ['reports', 'Reports', 'রিপোর্ট', ['/PayrollSalaryStatement', '/PayrollReconciliation']]
        ],
        'management:inventory': [
            ['settings', 'Settings', 'সেটিংস', ['/InventoryCatalog']],
            ['operations', 'Operations', 'কার্যক্রম', ['/InventoryMovements/Transactions', '/InventoryMovements/Adjustments', '/InventoryMovements/StockCounts', '/InventoryMovements/FacilityFinance', '/InventoryMovements/Index', '/AssetLifecycle']],
            ['reports', 'Reports', 'রিপোর্ট', ['/InventoryMovements/Reports']]
        ],
        'management:library': [
            ['settings', 'Settings', 'সেটিংস', ['/Library/Branches']],
            ['operations', 'Operations', 'কার্যক্রম', ['/Library/Index', '/Library/Circulation', '/Library/Reservations', '/Library/Digital', '/Library/Conditions']],
            ['reports', 'Reports', 'রিপোর্ট', ['/Library/Overdue', '/Library/Integrity', '/Library/Liabilities']]
        ],
        'management:communication': [
            ['settings', 'Settings', 'সেটিংস', ['/CommunicationAdministration', '/CustomFields', '/DocumentAdministration']],
            ['operations', 'Operations', 'কার্যক্রম', ['/CommunicationWorkspace']]
        ],
        'management:campus': [
            ['settings', 'Settings', 'সেটিংস', ['/HostelOperations/Setup', '/TransportOperations/Setup', '/FacilitySpaces']],
            ['operations', 'Operations', 'কার্যক্রম', ['/HostelOperations/Applications', '/HostelOperations/Allocations', '/HostelOperations/Index', '/hostel', '/TransportOperations/Assignments', '/TransportOperations/Index', '/TransportOperations/Trips', '/TransportOperations/Family', '/TransportOperations/DriverTrips', '/FacilityWorkOrders']],
            ['reports', 'Reports', 'রিপোর্ট', ['/HostelOperations/Reports', '/TransportOperations/Reports', '/OperationsInsights']]
        ]
    };

    // Common sidebar leaf-label parity for routes still carrying old English-only
    // Razor labels. The server remains authoritative for permission and URLs.
    const bengaliLabels = {
        '/platformtenants/index': 'প্রতিষ্ঠান পরিচালনা',
        '/platformcatalogadministration/index': 'প্রতিষ্ঠানের ধরন',
        '/platformfeatureadministration/index': 'ফিচার ক্যাটালগ',
        '/platformproductmoduleadministration/index': 'প্রোডাক্ট মডিউল',
        '/platforminstitutionmodulepresets/index': 'প্রতিষ্ঠানের মডিউল প্রিসেট',
        '/platformmodulefeatures/index': 'মডিউল ফিচার',
        '/subscriptionplanadministration/index': 'সাবস্ক্রিপশন প্ল্যান',
        '/legaldocuments/index': 'আইনি প্রকাশনা',
        '/subscriptionpaymenthistory/index': 'পেমেন্ট সমন্বয়ের ইতিহাস',
        '/tenantusers/index': 'ব্যবহারকারী ও ভূমিকা',
        '/tenantpermissionoverrides/index': 'সদস্যের বিশেষ অনুমতি',
        '/institutionmanagement/index': 'প্রতিষ্ঠান ও ক্যাম্পাস',
        '/institutionmanagement/profile': 'প্রতিষ্ঠানের প্রোফাইল',
        '/institutionmanagement/campuses': 'ক্যাম্পাস পরিচালনা',
        '/institutionmanagement/regional': 'ভাষা ও আঞ্চলিক সেটিংস',
        '/institutionmanagement/branding': 'প্রতিষ্ঠানের ব্র্যান্ডিং',
        '/institutionmanagement/subdomain': 'পোর্টাল সাবডোমেইন',
        '/institutionmanagement/history': 'পরিবর্তনের ইতিহাস',
        '/tenantdomains/index': 'নিজস্ব ডোমেইন',
        '/tenantterminology/index': 'প্রতিষ্ঠানের পরিভাষা',
        '/tenantsettings/index': 'প্রতিষ্ঠানের সেটিংস',
        '/numberseries/index': 'নম্বর সিরিজ',
        '/tenantmodules/index': 'প্রতিষ্ঠানের মডিউল',
        '/subscriptionbilling/index': 'সাবস্ক্রিপশন ও বিলিং',
        '/integrationadministration/index': 'ইন্টিগ্রেশন পরিচালনা',
        '/studentdocuments/index': 'শিক্ষার্থীর নথি',
        '/studentguardians/index': 'শিক্ষার্থীর অভিভাবক',
        '/studenttransferrequests/index': 'স্থানান্তর আবেদন ও ইনবক্স',
        '/enrollmentmanagement/index': 'শিক্ষার্থী ভর্তি',
        '/studentimports/index': 'শিক্ষার্থী আমদানি',
        '/studentreadmissions/index': 'শিক্ষার্থী পুনঃভর্তি',
        '/studentprogressions/index': 'উত্তরণ ও শ্রেণি পুনরাবৃত্তি',
        '/studentportal/index': 'শিক্ষার্থী ও অভিভাবক পোর্টাল',
        '/studentsubjects/index': 'ঐচ্ছিক বিষয়ের আবেদন',
        '/admissions/index': 'ভর্তির আবেদন',
        '/admissions/forms': 'ভর্তি ফরম সেটআপ',
        '/admissions/newapplication': 'নতুন ভর্তি আবেদন',
        '/admissions/applications': 'আবেদন যাচাই',
        '/admissions/assessments': 'ভর্তি পরীক্ষা ও মেধা',
        '/admissions/assessmentsetup': 'ভর্তি পরীক্ষা সেটআপ',
        '/admissions/merit': 'নম্বর ও মেধাতালিকা',
        '/admissions/offers': 'ভর্তির অফার',
        '/students/index': 'শিক্ষার্থী তালিকা',
        '/alumni/index': 'প্রাক্তন শিক্ষার্থী',
        '/learneridentities/index': 'শিক্ষার্থী পরিচয়',
        '/learneridentities/accesshistory': 'পরিচয় তথ্য দেখার ইতিহাস',
        '/learning/index': 'শিক্ষা ও কোর্স',
        '/learning/quizzes': 'কুইজ',
        '/learning/liveclasses': 'লাইভ ক্লাস',
        '/learning/coursecontent': 'কোর্সের বিষয়বস্তু',
        '/learning/assignmentfiles': 'অ্যাসাইনমেন্ট ফাইল',
        '/learning/certificates': 'সনদ',
        '/hosteloperations/applications': 'আবাসিক আবেদন',
        '/hosteloperations/allocations': 'শয্যা বরাদ্দ',
        '/hosteloperations/setup': 'কক্ষ ও শয্যা সেটআপ',
        '/hosteloperations/reports': 'হোস্টেল প্রতিবেদন',
        '/transportoperations/setup': 'রুট ও গাড়ি সেটআপ',
        '/transportoperations/assignments': 'পরিবহন বরাদ্দ',
        '/transportoperations/reports': 'পরিবহন প্রতিবেদন',
        '/inventorymovements/transactions': 'স্টক ইস্যু ও স্থানান্তর',
        '/inventorymovements/adjustments': 'স্টক সমন্বয়',
        '/inventorymovements/stockcounts': 'বাস্তব স্টক গণনা',
        '/inventorymovements/reports': 'স্টক প্রতিবেদন',
        '/library/reservations': 'বই সংরক্ষণ',
        '/library/branches': 'গ্রন্থাগার শাখা',
        '/library/overdue': 'বিলম্ব ও নোটিশ',
        '/library/conditions': 'বইয়ের অবস্থা',
        '/library/integrity': 'বইয়ের কপি যাচাই',
        '/library/liabilities': 'গ্রন্থাগারের দায়',
        '/assessmentadministration/index': 'পরীক্ষা ও ফলাফল',
        '/assessmentadministration/marks': 'নম্বর এন্ট্রি',
        '/assessmentadministration/results': 'ফলাফল যাচাই ও প্রকাশ',
        '/assessmentadministration/publications': 'প্রকাশিত ফলাফল ও ইতিহাস',
        '/assessmentadministration/markreviews': 'নম্বর জমা ও যাচাই',
        '/assessmentadministration/setup': 'মূল্যায়ন সেটআপ',
        '/assessmentadministration/attempts': 'পুনঃপরীক্ষা ও উন্নয়ন',
        '/assessmentadministration/resultrescrutiny': 'ফলাফল পুনর্নিরীক্ষণ',
        '/assessmentadministration/resultfollowup': 'ফলাফল পরবর্তী পর্যালোচনা',
        '/assessmentadministration/gradedistribution': 'গ্রেড বণ্টন',
        '/assessmentadministration/admitcards': 'পরীক্ষার প্রবেশপত্র',
        '/assessmentadministration/componentmarks': 'মূল্যায়নের নম্বর',
        '/assessmentadministration/subjectcompletions': 'বিষয় সম্পন্নের অবস্থা',
        '/assessmentadministration/assessmentcomponents': 'মূল্যায়নের উপাদান',
        '/assessmentadministration/assessmentschedules': 'পরীক্ষার সময়সূচি',
        '/assessmentadministration/gradeschemes': 'গ্রেডিং নিয়ম',
        '/assessmentadministration/certificatetemplates': 'সনদের টেমপ্লেট',
        '/assessmentadministration/certificates': 'সনদ',
        '/assessmentadministration/transcripts': 'নম্বরপত্র',
        '/academicroutines/index': 'ক্লাস রুটিন',
        '/academicinstruction/index': 'পাঠদান ও ক্লাস পরিচালনা',
        '/academiccalendar/index': 'একাডেমিক ক্যালেন্ডার',
        '/studentleaves/index': 'শিক্ষার্থীর ছুটি',
        '/attendance/index': 'উপস্থিতি',
        '/attendance/register': 'উপস্থিতি এন্ট্রি',
        '/attendance/reports': 'উপস্থিতির রিপোর্ট',
        '/attendance/corrections': 'উপস্থিতি সংশোধনের নিরীক্ষা',
        '/hr/index': 'এইচআর প্রশাসন',
        '/hr/directory': 'কর্মী তালিকা',
        '/hr/leaves': 'কর্মীর ছুটির আবেদন',
        '/hr/shiftassignments': 'কর্মীর শিফট',
        '/hr/campusassignments': 'কর্মীর ক্যাম্পাস বরাদ্দ',
        '/hr/bankaccounts': 'কর্মীর ব্যাংক হিসাব',
        '/hremployeerecords/index': 'কর্মী নিবন্ধন ও অবস্থা',
        '/employeecontracts/index': 'কর্মীর চুক্তি',
        '/recruitment/index': 'নিয়োগ ও কর্মী নির্বাচন',
        '/employeeattendance/index': 'কর্মীর উপস্থিতি',
        '/leaveentitlements/index': 'ছুটির প্রাপ্যতা',
        '/payrolloperations/index': 'বেতন ব্যবস্থাপনা',
        '/payrollsalarystructures/index': 'বেতন কাঠামো',
        '/payrollsalarycomponents/index': 'কর ও প্রভিডেন্ট ফান্ড',
        '/payrollbonuses/index': 'বোনাস রেজিস্টার',
        '/payrollloanregister/index': 'কর্মীর ঋণ',
        '/loandisbursement/index': 'ঋণ বিতরণ',
        '/loanrecovery/index': 'ঋণ আদায়',
        '/payrolljournalposting/index': 'বেতন GL পোস্টিং',
        '/payrollpaymentposting/index': 'বেতন পরিশোধ নিষ্পত্তি',
        '/payrollsalarystatement/index': 'বেতন বিবরণী',
        '/payrollreconciliation/index': 'বেতন সমন্বয়',
        '/feediscountrules/index': 'ফি ছাড়ের নিয়ম',
        '/admissioncashreceipts/index': 'ভর্তির নগদ রসিদ',
        '/feereceivablesaging/index': 'ফি বকেয়ার প্রতিবেদন',
        '/financeadjustments/index': 'আর্থিক সমন্বয়',
        '/financeadjustments/policies': 'ছাড় ও জরিমানার নিয়ম',
        '/financeadjustments/studentadjustments': 'শিক্ষার্থীর আর্থিক সমন্বয়',
        '/financeadjustments/fineregister': 'জরিমানার রেজিস্টার',
        '/financeadjustments/refunds': 'নগদ ফেরত ও নিষ্পত্তি',
        '/financeadjustments/hostelclearances': 'হোস্টেল নিষ্পত্তি',
        '/financeadjustments/transportclearances': 'পরিবহন নিষ্পত্তি',
        '/financeadjustments/reservationpreflight': 'রিজার্ভেশনের আর্থিক যাচাই',
        '/financeadjustments/discountawards': 'ছাড় অনুমোদনের রেজিস্টার',
        '/feeheadaccounting/index': 'ফি খাত ও GL ম্যাপিং',
        '/accountingsetup/index': 'হিসাবের খাত ও সময়কাল',
        '/accountingjournals/index': 'জার্নাল পোস্ট ও রিভার্সাল',
        '/accountingreports/index': 'জেনারেল লেজার ও ট্রায়াল ব্যালেন্স',
        '/inventorycatalog/index': 'ইনভেন্টরি ক্যাটালগ',
        '/inventorymovements/index': 'স্টক চলাচল',
        '/assetlifecycle/index': 'স্থায়ী সম্পদ ও রক্ষণাবেক্ষণ',
        '/hosteloperations/index': 'হোস্টেল পরিচালনা',
        '/transportoperations/index': 'পরিবহন পরিচালনা',
        '/operationsinsights/index': 'ক্যাম্পাস কার্যক্রম প্রতিবেদন',
        '/auditlogs/index': 'নিরীক্ষার লগ',
        '/importhistory/index': 'আমদানির ইতিহাস',
        '/communicationworkspace/index': 'যোগাযোগ কেন্দ্র',
        '/communicationadministration/index': 'যোগাযোগের সেটিংস',
        '/customfields/index': 'কাস্টম ফিল্ড',
        '/documentadministration/index': 'নথি পরিচালনা',
        '/library/index': 'গ্রন্থাগার',
        '/employeeportal/index': 'কর্মী পোর্টাল'
    };

    function normalizedPath(path) {
        if (typeof path !== 'string') return '/';
        const clean = path.split(/[?#]/, 1)[0].replace(/\/+$/, '');
        return (clean || '/').toLowerCase();
    }

    function navigationLabelFor(href, locale) {
        return String(locale || '').toLowerCase().startsWith('bn')
            ? (bengaliLabels[normalizedPath(href)] || null) : null;
    }

    function matchesPrefix(path, prefix) {
        const target = normalizedPath(path);
        const base = normalizedPath(prefix);
        return target === base || target.startsWith(base + '/');
    }

    function groupFor(section, href) {
        const path = normalizedPath(href);
        const definition = (groups[section] || []).find(([, , , prefixes]) =>
            prefixes.some(prefix => matchesPrefix(path, prefix)));
        return definition ? definition[0] : 'other';
    }

    function categoryFor(section, group, href) {
        const path = normalizedPath(href);
        // More-specific prefixes take priority (Offerings before AcademicManagement).
        const matches = (categories[`${section}:${group}`] || []).flatMap(([key, , , prefixes]) =>
            prefixes.filter(prefix => matchesPrefix(path, prefix)).map(prefix => ({ key, size: prefix.length })));
        return matches.sort((a, b) => b.size - a.size)[0]?.key || 'other';
    }

    function scoreActive(href, pathname, hash = '') {
        if (typeof href !== 'string' || !href.startsWith('/')) return -1;
        const [pathPart, linkHash = ''] = href.split('#', 2);
        const path = normalizedPath(pathPart);
        const current = normalizedPath(pathname);
        if (path === current) {
            // Prefer the exact anchor on an anchored page, otherwise its main link.
            return 10000 + path.length + (linkHash === hash.replace(/^#/, '') && linkHash ? 2000 : 0)
                - (linkHash && linkHash !== hash.replace(/^#/, '') ? 100 : 0);
        }
        const base = path.endsWith('/index') ? path.slice(0, -6) : path;
        return matchesPrefix(current, base) ? 100 + base.length : -1;
    }

    if (typeof module !== 'undefined' && module.exports) {
        module.exports = { groupFor, categoryFor, navigationLabelFor, scoreActive, normalizedPath };
    }
    if (typeof document === 'undefined') return;

    const sidebar = document.getElementById('sidebarMenu');
    if (!sidebar) return;
    const navs = [...sidebar.querySelectorAll('nav[data-sidebar-section]')];
    if (!navs.length) return;

    const bangla = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const storageKey = 'eduos.sidebar.navigation.v1';
    // Only presentation state is stored. The server-issued public session reference
    // and tenant identify the current rendered shell; neither grants access.
    // A different login or institution must never inherit another shell's menu state.
    const initialTenant = document.body?.dataset.authTenantId || '';
    const initialReference = document.body?.dataset.authSessionReference || '';
    const scope = initialTenant && /^[0-9A-F]{64}$/.test(initialReference)
        ? initialTenant + ':' + initialReference : '';
    const matchesScope = () => Boolean(scope) &&
        document.body?.dataset.authTenantId === initialTenant &&
        document.body?.dataset.authSessionReference === initialReference;
    function readState() {
        if (!matchesScope()) return { scroll: 0, group: '', category: '' };
        try {
            const state = JSON.parse(sessionStorage.getItem(storageKey) || '{}');
            if (state.scope !== scope) return { scroll: 0, group: '', category: '' };
            return {
                scroll: Number.isFinite(state.scroll) && state.scroll >= 0 ? state.scroll : 0,
                group: typeof state.group === 'string' ? state.group : '',
                category: typeof state.category === 'string' ? state.category : ''
            };
        } catch { return { scroll: 0, group: '', category: '' }; }
    }
    let state = readState();
    function saveState() {
        if (!matchesScope()) return;
        try { sessionStorage.setItem(storageKey, JSON.stringify({ ...state, scope })); }
        catch { /* Storage is optional. */ }
    }

    const allLinks = [];
    const allDetails = [];
    const allCategories = [];
    for (const nav of navs) {
        const section = nav.dataset.sidebarSection;
        const definitions = groups[section] || [];
        const links = [...nav.children].filter(node => node.matches?.('a.nav-link'));
        const categorized = new Map();
        const direct = [];
        for (const link of links) {
            const translated = navigationLabelFor(link.getAttribute('href') || '', bangla ? 'bn-BD' : 'en-BD');
            if (translated) {
                const label = link.querySelector('span');
                if (label) label.textContent = translated;
                else link.textContent = translated;
            }
            allLinks.push(link);
            const href = link.getAttribute('href') || '';
            if (matchesPrefix(href, '/Dashboard')) {
                direct.push(link);
                continue;
            }
            const key = groupFor(section, href);
            if (!categorized.has(key)) categorized.set(key, []);
            categorized.get(key).push(link);
        }

        const container = document.createDocumentFragment();
        for (const link of direct) {
            link.classList.add('sidebar-dashboard-link');
            container.append(link);
        }
        for (const [key, english, bengali] of [...definitions, ['other', 'Other', 'অন্যান্য', []]]) {
            const children = categorized.get(key);
            if (!children?.length) continue;
            const details = document.createElement('details');
            details.className = 'sidebar-group';
            details.dataset.sidebarGroup = `${section}:${key}`;
            const summary = document.createElement('summary');
            summary.className = 'sidebar-group-toggle';
            const label = document.createElement('span');
            label.className = 'sidebar-group-label';
            label.textContent = bangla ? bengali : english;
            const count = document.createElement('span');
            count.className = 'sidebar-group-count';
            count.textContent = String(children.length);
            count.setAttribute('aria-hidden', 'true');
            summary.append(label, count);
            const submenu = document.createElement('div');
            submenu.className = 'sidebar-submenu';
            const definitionsForGroup = categories[`${section}:${key}`];
            if (definitionsForGroup) {
                const byCategory = new Map();
                for (const child of children) {
                    const category = categoryFor(section, key, child.getAttribute('href') || '');
                    if (!byCategory.has(category)) byCategory.set(category, []);
                    byCategory.get(category).push(child);
                }
                for (const [categoryKey, en, bn] of [...definitionsForGroup, ['other', 'Other', 'অন্যান্য', []]]) {
                    const sublinks = byCategory.get(categoryKey);
                    if (!sublinks?.length) continue;
                    const subDetails = document.createElement('details');
                    subDetails.className = 'sidebar-category';
                    subDetails.dataset.sidebarCategory = `${section}:${key}:${categoryKey}`;
                    const subToggle = document.createElement('summary');
                    subToggle.className = 'sidebar-category-toggle';
                    const subLabel = document.createElement('span');
                    subLabel.textContent = bangla ? bn : en;
                    const subCount = document.createElement('span');
                    subCount.className = 'sidebar-category-count';
                    subCount.textContent = String(sublinks.length);
                    subCount.setAttribute('aria-hidden', 'true');
                    subToggle.append(subLabel, subCount);
                    const categoryLinks = document.createElement('div');
                    categoryLinks.className = 'sidebar-category-links';
                    for (const link of sublinks) categoryLinks.append(link);
                    subDetails.append(subToggle, categoryLinks);
                    submenu.append(subDetails);
                    allCategories.push(subDetails);
                }
            } else {
                for (const child of children) submenu.append(child);
            }
            details.append(summary, submenu);
            container.append(details);
            allDetails.push(details);
        }
        nav.replaceChildren(container);
        nav.classList.add('sidebar-nav-grouped');
        if (!links.length) {
            nav.hidden = true;
            const heading = nav.previousElementSibling;
            if (heading?.classList.contains('menu-title')) {
                heading.hidden = true;
                if (heading.previousElementSibling?.classList.contains('sidebar-divider')) {
                    heading.previousElementSibling.hidden = true;
                }
            }
        }
    }

    // The most specific link wins. Server-rendered 'active' is a safe fallback.
    function highlightCurrent() {
        const active = allLinks.reduce((best, link) => {
            const score = scoreActive(link.getAttribute('href') || '', window.location.pathname, window.location.hash);
            return score > best.score ? { link, score } : best;
        }, { link: null, score: -1 });
        const selectedLink = active.link || allLinks.find(link => link.classList.contains('active'));
        for (const link of allLinks) {
            link.classList.toggle('active', link === selectedLink);
            if (link === selectedLink) link.setAttribute('aria-current', 'page');
            else link.removeAttribute('aria-current');
        }
        for (const details of allDetails) {
            details.classList.toggle('has-current', details.contains(selectedLink));
        }
        for (const details of allCategories) {
            details.classList.toggle('has-current', details.contains(selectedLink));
        }
        return selectedLink;
    }

    let selected = highlightCurrent();
    let activeGroup = selected?.closest('.sidebar-group') || null;
    const isTop = () => window.matchMedia('(min-width: 992px)').matches &&
        document.documentElement.dataset.workspaceNavigation === 'top';

    function restoreOpenGroup() {
        const toOpen = isTop() ? null : (activeGroup ||
            allDetails.find(details => details.dataset.sidebarGroup === state.group));
        for (const details of allDetails) details.open = details === toOpen;
        if (!isTop()) {
            const selectedCategory = selected?.closest('.sidebar-category');
            const toOpenCategory = selectedCategory ||
                allCategories.find(details => details.dataset.sidebarCategory === state.category && toOpen?.contains(details));
            for (const details of allCategories) details.open = details === toOpenCategory;
        }
    }
    restoreOpenGroup();

    for (const details of allDetails) {
        details.addEventListener('toggle', () => {
            if (!details.open) return;
            for (const other of allDetails) if (other !== details) other.open = false;
            state.group = details.dataset.sidebarGroup;
            saveState();
        });
    }
    for (const details of allCategories) {
        details.addEventListener('toggle', () => {
            if (!details.open) return;
            for (const other of allCategories) {
                if (other !== details && other.parentElement === details.parentElement) other.open = false;
            }
            state.category = details.dataset.sidebarCategory;
            saveState();
        });
    }

    let scheduled = false;
    sidebar.addEventListener('scroll', () => {
        if (scheduled || isTop()) return;
        scheduled = true;
        window.requestAnimationFrame(() => {
            scheduled = false;
            state.scroll = sidebar.scrollTop;
            saveState();
        });
    }, { passive: true });
    for (const link of allLinks) link.addEventListener('click', () => {
        state.scroll = sidebar.scrollTop;
        state.group = link.closest('.sidebar-group')?.dataset.sidebarGroup || state.group;
        state.category = link.closest('.sidebar-category')?.dataset.sidebarCategory || state.category;
        saveState();
    });
    window.addEventListener('pagehide', () => {
        if (!isTop()) state.scroll = sidebar.scrollTop;
        saveState();
    });

    function showActiveAndRestoreScroll() {
        if (isTop()) return;
        sidebar.scrollTop = state.scroll;
        if (!selected) return;
        const navRect = sidebar.getBoundingClientRect();
        const itemRect = selected.getBoundingClientRect();
        // Scroll only the navigation pane, never the page, if its selected link is hidden.
        if (itemRect.top < navRect.top + 12) sidebar.scrollTop -= navRect.top + 12 - itemRect.top;
        else if (itemRect.bottom > navRect.bottom - 12) sidebar.scrollTop += itemRect.bottom - navRect.bottom + 12;
        state.scroll = sidebar.scrollTop;
        saveState();
    }
    window.requestAnimationFrame(showActiveAndRestoreScroll);
    window.addEventListener('hashchange', () => {
        selected = highlightCurrent();
        activeGroup = selected?.closest('.sidebar-group') || null;
        restoreOpenGroup();
        window.requestAnimationFrame(showActiveAndRestoreScroll);
    });
    window.addEventListener('eduos:workspace-preferences', () => {
        if (isTop()) {
            allDetails.forEach(details => { details.open = false; });
            allCategories.forEach(details => { details.open = false; });
        } else {
            restoreOpenGroup();
            window.requestAnimationFrame(showActiveAndRestoreScroll);
        }
    });

    // Native <summary> handles Enter/Space. Arrow/Home/End provide predictable
    // three-level keyboard traversal without focusing collapsed or unauthorized links.
    sidebar.addEventListener('keydown', event => {
        if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key) ||
            event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
        const focused = event.target.closest?.('summary, a.nav-link');
        const nav = focused?.closest('nav[data-sidebar-section]');
        if (!nav || !sidebar.contains(nav)) return;
        const targets = [...nav.querySelectorAll('summary, a.nav-link')].filter(element =>
            element.getClientRects().length > 0 &&
            !element.closest('[hidden], [inert]'));
        const index = targets.indexOf(focused);
        if (index < 0) return;
        const next = event.key === 'Home' ? 0 :
            event.key === 'End' ? targets.length - 1 :
            event.key === 'ArrowDown' ? Math.min(index + 1, targets.length - 1) :
            Math.max(index - 1, 0);
        event.preventDefault();
        targets[next]?.focus();
    });

    document.addEventListener('pointerdown', event => {
        if (!isTop() || sidebar.contains(event.target)) return;
        for (const details of allDetails) details.open = false;
        for (const details of allCategories) details.open = false;
    });
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || !isTop()) return;
        const open = allDetails.find(details => details.open);
        if (!open) return;
        open.open = false;
        if (sidebar.contains(event.target)) open.querySelector('summary')?.focus();
    });
})();
