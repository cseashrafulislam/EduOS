const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
const css = read('EduOS.App/wwwroot/css/site.css');
const js = read('EduOS.App/wwwroot/js/platform/page-context-help.js');
const { guideFor, coverage, normalizeRoute, validateGuide, helpContextStatus } =
    require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');
const routes = ['/Account/Profile', '/FeeDiscountAttributionReview/Index', '/FeeInvoiceCoverage/Index', '/FeeInstallmentProjection/Index', '/FeeDueCalendar/Index', '/FeeReceivablesAging/Index', '/PayrollJournalPosting/Index', '/PayrollSalaryStatement/Index', '/Alumni/Index', '/CareerEmployers/Index', '/CareerOpportunities/Index', '/CareerPlacements/Index', '/FeeOperations/Index', '/Students/Index', '/Attendance/Index', '/PayrollOperations/Index', '/FacilityWorkOrders/Index', '/FacilitySpaces/Index', '/Admissions/Index', '/StudentInvoicePosting/Index', '/workspace', '/Account/BrandingSetup', '/StudentLifecycle/Index', '/Account/Login',
    ...['Years','Terms','Departments','Classes','Sections','Rooms','Subjects','Curricula','Prerequisites','Offerings'].map(action => '/AcademicManagement/' + action),
    ...['Structures','Billing','Ledger'].map(action => '/FeeOperations/' + action), '/HR/Directory', '/HR/Leaves',
    ...['Policies','StudentAdjustments','FineRegister','Refunds'].map(action => '/FinanceAdjustments/' + action),
    ...['Marks','Results','Publications','Setup','MarkReviews','Attempts','ResultRescrutiny',
        'ResultFollowUp','GradeDistribution','ExamReports','AdmitCards'].map(action => '/AssessmentAdministration/' + action),
    ...['Forms','NewApplication','Applications','Assessments','AssessmentSetup','Merit','Offers'].map(action => '/Admissions/' + action),
    ...['Register','Reports','Corrections'].map(action => '/Attendance/' + action),
    ...['Profile','Campuses','Regional','Branding','Subdomain','History'].map(action => '/InstitutionManagement/' + action),
    ...['TenantUsers','TenantPermissionOverrides','TenantModules','NumberSeries','TenantDomains','TenantTerminology',
        'TenantSettings','SubscriptionBilling','IntegrationAdministration','StudentDocuments','StudentGuardians',
        'EnrollmentManagement'].map(name => '/' + name + '/Index'),
    ...['FeeDiscountRules','AdmissionCashReceipts','FeeHeadAccounting','AccountingSetup','AccountingJournals',
        'AccountingReports','InventoryCatalog','AssetLifecycle','OperationsInsights','AcademicRoutines',
        'AcademicInstruction','AcademicCalendar','StudentLeaves','EmployeeAttendance','LeaveEntitlements',
        'StudentPortal','StudentSubjects','LearnerIdentities','Learning','AuditLogs','ImportHistory',
        'CommunicationAdministration','CustomFields','DocumentAdministration','CommunicationWorkspace']
        .map(name => '/' + name + '/Index')];

test('all reviewed route manuals have full bilingual structure, matching steps and examples', () => {
    assert.deepEqual(coverage(), { routes: 136, en: 136, bn: 136 });
    for (const route of routes) {
        const en = guideFor(route, 'en-BD');
        const bn = guideFor(route, 'bn-BD');
        assert.ok(en && bn, route);
        assert.equal(en.version, bn.version);
        assert.equal(en.steps.length, bn.steps.length);
        assert.ok(en.steps.length >= 3);
        for (const field of ['title','purpose','before','example','result','rules','recovery']) {
            assert.ok(en[field].trim() && bn[field].trim(), route + ':' + field);
            assert.doesNotMatch(en[field] + bn[field], /<[^>]*>/);
        }
        assert.deepEqual(en.related.map(x => x.path), bn.related.map(x => x.path));
        assert.notEqual(en.purpose, bn.purpose);
    }
});
test('visitor occupancy manual v5 covers overdue alerts and campus time-policy recovery', () => {
    const view = read('EduOS.App/Views/FacilityVisitors/Index.cshtml');
    const en = guideFor('/FacilityVisitors/Index', 'en-BD');
    const bn = guideFor('/FacilityVisitors/Index', 'bn-BD');
    assert.equal(en.version, 5);
    assert.equal(bn.version, 5);
    assert.match(view, /id="fvOccupancy"/);
    assert.match(en.steps.join(' '), /View occupancy/);
    assert.match(bn.steps.join(' '), /উপস্থিতি দেখুন/);
    assert.match(en.rules, /not physical anti-tailgating/);
    assert.match(en.steps.join(' '), /overdue checked-in counts/);
    assert.match(en.rules, /EarlyCheckInMinutes/);
    assert.match(bn.steps.join(' '), /সময় পার হওয়া/);
    assert.match(bn.recovery, /উপস্থিতির সীমা পূর্ণ/);
});
test('unverified and malicious routes cannot inherit a generic or privileged manual', () => {
    for (const route of ['/Admin/Index', '/FeeOperations/Other', '/FeeOperations/Index?tenant=2',
        '//evil.example', '/PayrollOperations/Index#private', '/']) {
        assert.equal(guideFor(route, 'en-BD'), null, route);
    }
    assert.equal(normalizeRoute('/Students'), '/students/index');
    assert.equal(normalizeRoute('/Students/Index'), '/students/index');
    assert.equal(validateGuide({ reviewed: false, version: 1 }), false);
});
test('twelve administration and student manuals match protected page controls and bilingual steps', () => {
    const protectedPages = [
        ['TenantUsers', 'memberSearch', 'TenantAdmin'],
        ['TenantPermissionOverrides', 'permissionOverrideSave', 'TenantAdmin'],
        ['TenantModules', 'moduleList', 'TenantAdmin'],
        ['NumberSeries', 'nsForm', 'TenantAdmin'],
        ['TenantDomains', 'domainRegistration', 'TenantAdmin'],
        ['TenantTerminology', 'ttForm', 'TenantAdmin'],
        ['TenantSettings', 'tsForm', 'TenantAdmin'],
        ['SubscriptionBilling', 'sbInvoiceRows', 'TenantAdmin'],
        ['IntegrationAdministration', 'iaDeliveryExport', 'TenantAdmin'],
        ['StudentDocuments', 'documentAttach', 'TenantAdmin'],
        ['StudentGuardians', 'guardianCreate', 'TenantAdmin'],
        ['EnrollmentManagement', 'enrollmentSubmit', 'TenantAdmin']
    ];
    for (const [name, id, role] of protectedPages) {
        const view = read(`EduOS.App/Views/${name}/Index.cshtml`);
        const controller = read(`EduOS.App/Controllers/${name}Controller.cs`);
        const en = guideFor(`/${name}/Index`, 'en-BD');
        const bn = guideFor(`/${name}/Index`, 'bn-BD');
        assert.ok(view.includes(`id="${id}"`), `${name}: missing referenced UI`);
        assert.match(controller, new RegExp(`Authorize\\(Roles = "${role}"\\)`));
        assert.ok(en && bn, `${name}: guide absent`);
        assert.equal(en.steps.length, 3, name);
        assert.equal(bn.steps.length, 3, name);
        assert.notEqual(en.example, bn.example, name);
        assert.deepEqual(en.related.map(link => link.path), bn.related.map(link => link.path));
    }
});
test('high-traffic institution settings and subscription UI honor selected document language', () => {
    for (const name of ['NumberSeries', 'TenantDomains', 'TenantTerminology', 'TenantModules', 'SubscriptionBilling']) {
        const view = read(`EduOS.App/Views/${name}/Index.cshtml`);
        assert.match(view, /CultureInfo\.CurrentUICulture\.Name == "bn-BD"/, name);
        assert.match(view, /ViewData\["Title"\] = bn \?/, name);
        assert.match(view, /@\(bn \?/, name);
    }
    for (const file of ['number-series/index.js', 'platform/tenant-domains.js',
        'platform/tenant-terminology.js', 'subscription-billing/index.js']) {
        const source = read(`EduOS.App/wwwroot/js/${file}`);
        assert.match(source, /document\.documentElement\.lang/, file);
        assert.match(source, /"bn"/, file);
    }
    const subscription = read('EduOS.App/wwwroot/js/subscription-billing/index.js');
    assert.match(subscription, /t\("Download CSV","CSV ডাউনলোড"\)/);
    assert.match(subscription, /t\("View receipt","রসিদ দেখুন"\)/);
    assert.match(subscription, /typed!=="END"/);
    assert.match(subscription, /Number\(pay\.state\)===3 && pay\.completedAt/);
});
test('twenty-five audited finance, academic and communication manuals match real authorized screens', () => {
    const protectedPages = [
        ['FeeDiscountRules', 'discountRuleSave'], ['AdmissionCashReceipts', 'admissionCashCollect'],
        ['FeeHeadAccounting', 'feeHeadAccounting'], ['AccountingSetup', 'accountingSetup'],
        ['AccountingJournals', 'accountingJournals'], ['AccountingReports', 'institutionAccountingReports'],
        ['InventoryCatalog', 'itemForm'], ['AssetLifecycle', 'assetWorkspace'],
        ['OperationsInsights', 'operationsInsightsTitle'], ['AcademicRoutines', 'routineLoad'],
        ['AcademicInstruction', 'instructionLoad'], ['AcademicCalendar', 'calendarLoad'],
        ['StudentLeaves', 'leaveSubmit'], ['EmployeeAttendance', 'employeeAttendanceSave'],
        ['LeaveEntitlements', 'leaveEntitlementTypeReset'], ['StudentPortal', 'studentPortalTitle'],
        ['StudentSubjects', 'subjectsRequestButton'], ['LearnerIdentities', 'identitySubmit'],
        ['Learning', 'learningCourse'], ['AuditLogs', 'auditTitle'], ['ImportHistory', 'importHistoryTitle'],
        ['CommunicationAdministration', 'communicationTitle'], ['CustomFields', 'cfPage'],
        ['DocumentAdministration', 'docAdminTitle'], ['CommunicationWorkspace', 'cwSendMessage']
    ];
    for (const [name, id] of protectedPages) {
        const view = read(`EduOS.App/Views/${name}/Index.cshtml`);
        const controller = read(`EduOS.App/Controllers/${name}Controller.cs`);
        const en = guideFor(`/${name}/Index`, 'en-BD');
        const bn = guideFor(`/${name}/Index`, 'bn-BD');
        assert.ok(view.includes(`id="${id}"`), `${name}: missing UI control`);
        assert.match(controller, /\[Authorize(?:\(|\])/, `${name}: protected page`);
        assert.ok(en && bn, `${name}: no bilingual guide`);
        assert.ok(en.steps.length >= 3, `${name}: insufficient steps`);
        assert.deepEqual(en.related.map(x => x.path), bn.related.map(x => x.path));
        assert.notEqual(en.example, bn.example, `${name}: bilingual example`);
    }
});
test('academic and accounting pages use the one shared manual rather than duplicate help icons', () => {
    const report = read('EduOS.App/Views/AccountingReports/Index.cshtml');
    const routine = read('EduOS.App/Views/AcademicRoutines/Index.cshtml');
    const instruction = read('EduOS.App/Views/AcademicInstruction/Index.cshtml');
    const instructionJs = read('EduOS.App/wwwroot/js/academic/instruction.js');
    assert.doesNotMatch(report, /Accounting report help|Open Journal Posting &amp; Reversal/);
    assert.doesNotMatch(routine, /Routine field guide|ⓘ/);
    assert.doesNotMatch(instruction, /id="instructionHelp"|id="instructionHelpText"|ⓘ/);
    assert.doesNotMatch(instructionJs, /get\('instructionHelp'\)/);
    assert.match(layout, /id="pageContextHelp"/);
    for (const route of ['/AcademicRoutines/Index', '/AcademicInstruction/Index', '/AccountingReports/Index'])
        assert.ok(guideFor(route, 'bn-BD'), route);
});
test('shared drawer is one user-initiated accessible renderer, not hover-triggered popover', () => {
    assert.match(layout, /id="pageContextHelp"[^>]+type="button"/);
    assert.match(layout, /aria-controls="pageHelpDrawer"/);
    assert.match(layout, /<dialog class="page-help-drawer" id="pageHelpDrawer" aria-labelledby="pageHelpTitle" aria-modal="true"/);
    assert.match(layout, /id="pageHelpContent"/);
    assert.match(layout, /page-context-help\.js" asp-append-version="true"/);
    assert.match(js, /dialog\.showModal\(\)/);
    assert.match(js, /trigger\.addEventListener\('click'/);
    assert.match(js, /dialog\.addEventListener\('close', \(\) => \{ if \(status\(\) === 'ready'\) trigger\.focus\(\); \}\)/);
    assert.doesNotMatch(js, /mouseenter|setTimeout|innerHTML|eval\(|fetch\(|localStorage/);
    assert.match(js, /document\.querySelectorAll\('nav\[data-sidebar-section\] a\.nav-link, a\[data-feature-discovery-link\]'\)/);
    assert.match(js, /normalizeRoute\(href\) === target/);
    assert.match(css, /\.page-help-drawer::backdrop/);
    assert.match(css, /max-width: 575\.98px/);
    assert.match(css, /data-workspace-theme="dark"\] \.page-help-drawer/);
    assert.match(css, /prefers-color-scheme: dark/);
});
test('the pilot removes duplicate header Info controls but preserves workflow operations', () => {
    const fees = read('EduOS.App/Views/FeeOperations/Index.cshtml');
    const students = read('EduOS.App/Views/Students/Index.cshtml');
    const attendance = read('EduOS.App/Views/Attendance/Index.cshtml');
    const payroll = read('EduOS.App/Views/PayrollOperations/Index.cshtml');
    assert.doesNotMatch(fees, /aria-label="Fee billing guide"/);
    assert.doesNotMatch(students, /id="studentDirectoryHelp"/);
    assert.doesNotMatch(attendance, /aria-label="Attendance help"/);
    for (const id of ['feePreview','feeGenerate','feeCollect']) assert.match(fees, new RegExp('id="' + id + '"'));
    for (const id of ['studentSearch','studentExportPage']) assert.match(students, new RegExp('id="' + id + '"'));
    for (const id of ['saveAttendance','finalizeAttendance']) assert.match(attendance, new RegExp('id="' + id + '"'));
    assert.match(payroll, /id="payrollGenerate"/);
    assert.match(layout, /data-language-selector/);
});


test('shared localization catalogue has exact en/bn key parity and pilot Razor keys resolve', () => {
    const en = read('EduOS.App/Resources/Localization/SharedResource.resx');
    const bn = read('EduOS.App/Resources/Localization/SharedResource.bn-BD.resx');
    const keys = xml => new Set([...xml.matchAll(/<data\s+name="([^"]+)"/g)].map(x => x[1]));
    const enKeys = keys(en), bnKeys = keys(bn);
    assert.ok(enKeys.size >= 800, 'unexpectedly small resource catalogue');
    assert.deepEqual([...enKeys].sort(), [...bnKeys].sort(), 'raw translation keys would leak in one language');
    for (const page of ['EduOS.App/Views/Shared/_Layout.cshtml',
        'EduOS.App/Views/Students/Index.cshtml', 'EduOS.App/Views/Admissions/Index.cshtml']) {
        for (const match of read(page).matchAll(/@T\["([^"]+)"\]/g))
            assert.ok(enKeys.has(match[1]), page + ': ' + match[1]);
    }
});
test('language switch posts antiforgery and returns to the current local route', () => {
    const controller = read('EduOS.App/Controllers/LocalizationController.cs');
    const cookie = read('EduOS.App/Localization/PreferredCultureCookie.cs');
    assert.match(layout, /<html lang="@currentCulture"/);
    assert.match(layout, /<form asp-controller="Localization" asp-action="SetLanguage" method="post"/);
    assert.match(layout, /name="returnUrl" value="@returnUrl"/);
    assert.match(layout, /value="en-BD"/);
    assert.match(layout, /value="bn-BD"/);
    assert.match(controller, /\[ValidateAntiForgeryToken\]/);
    assert.match(controller, /PreferredCultureCookie\.TryNormalize/);
    assert.match(controller, /PreferredCultureCookie\.Write/);
    assert.match(controller, /Url\.IsLocalUrl\(returnUrl\)/);
    assert.match(cookie, /bn-BD/);
    assert.match(cookie, /en-BD/);
    assert.match(js, /document\.documentElement\.lang/);
});

test('facilities manual follows actual independent return, correction and non-posting contract', () => {
    const en = guideFor('/FacilityWorkOrders/Index', 'en-BD');
    const bn = guideFor('/FacilityWorkOrders/Index', 'bn-BD');
    assert.ok(en && bn);
    assert.match(en.steps.join(' '), /Return for correction/);
    assert.match(en.steps.join(' '), /scheduled start and before its end/);
    assert.match(bn.steps.join(' '), /সরবরাহকারীর বুকিং অনুমোদিত/);
    assert.match(bn.steps.join(' '), /সংশোধনের জন্য ফেরত/);
    assert.match(en.rules, /does not issue spare parts or post Finance GL/);
    assert.match(en.recovery, /409/);
    assert.deepEqual(en.related.map(x => x.path), ['/FacilitySpaces/Index']);
});

test('admission manual matches published intake, review, verified offer and enrollment source actions', () => {
    const view = read('EduOS.App/Views/Admissions/Index.cshtml');
    const code = read('EduOS.App/wwwroot/js/admissions/index.js');
    const controller = read('EduOS.App/Controllers/AdmissionsController.cs');
    const api = read('EduOS.App/Controllers/Api/AdmissionApplicationsController.cs');
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,AdmissionOfficer"\)/);
    assert.match(controller, /RequireModule\("ADMISSION"\)/);
    for (const id of ['admissionFormReference', 'submitApplicationButton', 'applicationList', 'reviewStatus',
        'saveDecisionButton', 'admitApplicantButton', 'completeEnrollmentButton'])
        assert.ok(view.includes('id="' + id + '"'), 'missing admission control ' + id);
    for (const action of ['selectAdmissionForm', 'submitApplication', 'saveDecision', 'prepareEnrollment', 'completeEnrollment'])
        assert.ok(code.includes('function ' + action), 'missing workflow ' + action);
    assert.match(api, /offer\/verified-acceptance/);
    assert.match(api, /\[HttpPost\("\{reference:guid\}\/admit"\)\]/);
    const en = guideFor('/Admissions/Index', 'en-BD');
    const bn = guideFor('/Admissions/Index', 'bn-BD');
    assert.match(en.steps.join(' '), /Published intake/);
    assert.match(en.rules, /request identifier/);
    assert.match(bn.steps.join(' '), /অভিভাবকের/);
});
test('invoice posting manual reflects view-only Accountant and authorized canonical GL post', () => {
    const view = read('EduOS.App/Views/StudentInvoicePosting/Index.cshtml');
    const js = read('EduOS.App/wwwroot/js/finance/student-invoice-posting.js');
    const controller = read('EduOS.App/Controllers/Api/StudentInvoicePostingController.cs');
    const service = read('EduOS.Service/Services/Finance/StudentInvoicePostingService.cs');
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,Principal,Accountant"\)/);
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,Principal"\)/);
    assert.match(controller, /RequireModule\("FINANCE"\)/);
    assert.match(controller, /AutoValidateAntiforgeryToken/);
    for (const id of ['invoicePostingPeriod', 'invoicePostingUnposted', 'invoicePostingRows', 'invoicePostingNotice'])
        assert.ok(view.includes('id="' + id + '"'), 'missing posting control ' + id);
    assert.match(js, /periodCovers\(row, periodId\)/);
    assert.match(js, /button\.textContent = 'Post GL'/);
    assert.match(service, /private bool CanPost/);
    assert.match(service, /PostingKey\(long tenantId, Guid invoice\)/);
    const en = guideFor('/StudentInvoicePosting/Index', 'en-BD');
    const bn = guideFor('/StudentInvoicePosting/Index', 'bn-BD');
    assert.match(en.rules, /Accountants can inspect but cannot Post GL/);
    assert.match(en.example, /No cash receipt is created/);
    assert.match(bn.recovery, /রিলোড/);
});

test('alumni manual follows verified consent, event approval, RSVP and attendance source contracts', () => {
    const controller = read('EduOS.App/Controllers/AlumniController.cs');
    const api = read('EduOS.App/Controllers/Api/AlumniApiController.cs');
    const view = read('EduOS.App/Views/Alumni/Index.cshtml');
    const client = read('EduOS.App/wwwroot/js/alumni/index.js');
    const consent = read('EduOS.Service/Services/Alumni/AlumniService.cs');
    const events = read('EduOS.Service/Services/Alumni/AlumniEventsService.cs');
    const attendance = read('EduOS.Service/Services/Alumni/AlumniAttendanceService.cs');
    assert.match(controller, /RequireModule\("ALUMNI"\)/);
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin"\)/);
    for (const id of ['alumniGraduate', 'alumniOptIn', 'alumniSave', 'alumniEventForm',
        'alumniEventCards', 'alumniEventsExport', 'alumniRows', 'alumniExport'])
        assert.ok(view.includes('id="' + id + '"'), 'missing alumni control ' + id);
    for (const route of ['consent', 'directory.csv', 'events/rsvp', 'events/report.csv'])
        assert.ok(api.includes('"' + route + '"'), 'missing alumni API ' + route);
    for (const action of ['saveRsvp', 'createEvent', 'markAttendance', 'loadDirectory'])
        assert.ok(client.includes('function ' + action), 'missing alumni workflow ' + action);
    assert.match(consent, /DELETE FROM dbo\.AlumniEventRsvps/);
    assert.match(consent, /StudentExitType\.Completed/);
    assert.match(events, /current\.CreatedByUserId == actor/);
    assert.match(attendance, /CheckedInAtUtc/);
    const en = guideFor('/Alumni/Index', 'en-BD');
    const bn = guideFor('/Alumni/Index', 'bn-BD');
    assert.equal(en.steps.length, 7);
    assert.equal(bn.steps.length, 7);
    assert.match(en.rules, /each channel starts OFF/);
    assert.match(en.rules, /distinct consent/);
    assert.match(en.rules, /campus grant/);
    assert.match(en.steps.join(' '), /another|different administrator/);
    assert.match(bn.rules, /পৃথক সম্মতি/);
    assert.match(en.steps.join(' '), /Send next 5/);
    assert.match(en.recovery, /same request ID/);
    assert.deepEqual(en.related.map(x => x.path), ['/Students/Index']);
});

test('aging manual matches authorized read-only reports and bounded CSV', () => {
 const view=read('EduOS.App/Views/FeeReceivablesAging/Index.cshtml');
 const page=read('EduOS.App/Controllers/FeeReceivablesAgingController.cs');
 const api=read('EduOS.App/Controllers/Api/FeeReceivablesAgingController.cs');
 assert.match(page, /Authorize\(Roles = "TenantAdmin,Principal,Accountant"\)/);
 assert.match(page, /RequireModule\("FINANCE"\)/);
 for(const route of ['reconciliation','payment-reconciliation','daily-cash-collections'])
   assert.ok(api.includes('[HttpGet("'+route+'")]'),route);
 for(const id of ['agingRun','agingExport','reconcileRun','paymentReconcileRun','dailyCashRun','dailyCashExport'])
   assert.ok(view.includes('id="'+id+'"'),id);
 assert.match(guideFor('/FeeReceivablesAging/Index','en-BD').rules,/does not reconstruct historical balances/);
 assert.match(guideFor('/FeeReceivablesAging/Index','bn-BD').steps.join(' '),/নগদ/);
 assert.doesNotMatch(view,/aria-label="Cash collection report help"/);
});
test('payroll journal manual matches independent approval, canonical GL and reversal', () => {
 const page=read('EduOS.App/Controllers/PayrollJournalPostingController.cs');
 const api=read('EduOS.App/Controllers/Api/PayrollJournalPostingController.cs');
 const view=read('EduOS.App/Views/PayrollJournalPosting/Index.cshtml');
 const service=read('EduOS.Service/Services/HR/PayrollJournalPostingService.cs');
 assert.match(page,/Authorize\(Roles = "TenantAdmin,Principal,Accountant"\)/);
 assert.match(page,/RequireModule\("FINANCE"\), RequireModule\("PAYROLL"\)/);
 assert.match(api,/AutoValidateAntiforgeryToken/);
 assert.match(api,/Authorize\(Roles = "TenantAdmin"\)/);
 for(const route of ['reverse/request','reverse/review','reverse/post'])assert.ok(api.includes(route),route);
 for(const id of ['payrollPostingMappings','payrollPostingConfirm','payrollReversalForm'])
   assert.ok(view.includes('id="'+id+'"'),id);
 assert.match(service,/Payroll must be independently approved before GL posting/);
 assert.match(guideFor('/PayrollJournalPosting/Index','en-BD').rules,/Maker and approver must differ/);
 assert.match(guideFor('/PayrollJournalPosting/Index','bn-BD').steps.join(' '),/বিপরীত জার্নাল/);
});
test('salary statement manual matches snapshot report and bounded export without duplicate Info', () => {
 const view=read('EduOS.App/Views/PayrollSalaryStatement/Index.cshtml');
 const page=read('EduOS.App/Controllers/PayrollSalaryStatementController.cs');
 const api=read('EduOS.App/Controllers/Api/PayrollSalaryStatementController.cs');
 const service=read('EduOS.Service/Services/HR/PayrollSalaryStatementService.cs');
 const client=read('EduOS.App/wwwroot/js/payroll/salary-statement.js');
 assert.match(page,/Authorize\(Roles = "TenantAdmin"\)/);
 assert.match(page,/RequireModule\("PAYROLL"\)/);
 assert.match(api,/\[HttpGet\("csv"\)\]/);
 assert.match(api,/GetAsync\(year, month, campusId, 1, 1000, ct\)/);
 assert.match(service,/pageSize > 50 && count > 1000/);
 assert.match(client,/pageSize:'25'/);
 for(const id of ['statementYear','statementMonth','statementCampus','statementCsv','statementPrint'])
   assert.ok(view.includes('id="'+id+'"'),id);
 assert.doesNotMatch(view,/aria-label="Statement help"/);
 assert.match(guideFor('/PayrollSalaryStatement/Index','en-BD').rules,/not current salary structures/);
 assert.match(guideFor('/PayrollSalaryStatement/Index','bn-BD').steps.join(' '),/২৫/);
});

test('institution branding manual matches real TenantAdmin and CSRF-protected writes', () => {
    const account = read('EduOS.App/Controllers/AccountController.cs');
    const profile = read('EduOS.App/Controllers/Api/TenantProfileController.cs');
    const settings = read('EduOS.App/Controllers/Api/TenantSettingController.cs');
    const view = read('EduOS.App/Views/Account/BrandingSetup.cshtml');
    const client = read('EduOS.App/wwwroot/js/account/branding-setup.js');
    assert.match(account, /Authorize\(Roles = "TenantAdmin"\)[\s\S]*?IActionResult BrandingSetup/);
    assert.match(profile, /AutoValidateAntiforgeryToken/);
    assert.match(settings, /AutoValidateAntiforgeryToken/);
    assert.match(view, /id="brandingForm"[\s\S]*?@Html\.AntiForgeryToken\(\)/);
    assert.match(client, /function mutationHeaders\(headers = \{\}\)/);
    assert.match(client, /RequestVerificationToken: token/);
    assert.equal((client.match(/headers: mutationHeaders\(/g) || []).length, 5);
    for (const id of ['subdomainInput', 'saveSubdomainBtn', 'logoInput', 'faviconInput',
        'primaryColorText', 'secondaryColorText', 'accentColorText', 'saveBrandingBtn'])
        assert.ok(view.includes('id="' + id + '"'), id);
    const en = guideFor('/Account/BrandingSetup', 'en-BD');
    const bn = guideFor('/Account/BrandingSetup', 'bn-BD');
    assert.equal(en.steps.length, 5);
    assert.equal(bn.steps.length, 5);
    assert.match(en.rules, /not one atomic publication/);
    assert.match(bn.recovery, /রিলোড/);
    assert.deepEqual(en.related.map(x => x.path), ['/workspace']);
});

test('branding color saves carry CSRF; missing token and invalid colors fail before any write', async () => {
    const script = read('EduOS.App/wwwroot/js/account/branding-setup.js');
    async function scenario(token, invalid = false, sessionMatches = true) {
        const controls = new Map();
        const el = id => {
            if (!controls.has(id)) controls.set(id, {
                value: '', disabled: false, hidden: false, textContent: '',
                listeners: {}, addEventListener(type, fn) { this.listeners[type] = fn; },
                focus() {}, replaceChildren() {}, setAttribute() {}
            });
            return controls.get(id);
        };
        const form = el('brandingForm');
        const save = el('saveBrandingBtn');
        save.dataset = { idleLabel: 'Save', loadingLabel: 'Saving' };
        const alert = el('alertContainer');
        let onReady;
        const calls = [];
        const document = {
            body: { dataset: { authTenantId: '1', authSessionReference: 'current' } },
            getElementById(id) {
                if (['eduosSessionShield', 'brandingSetupStrings', 'subdomainInput', 'logoPreview', 'faviconPreview',
                    'logoInput', 'faviconInput', 'removeLogoBtn', 'removeFaviconBtn'].includes(id))
                    return null;
                return el(id);
            },
            querySelector(selector) {
                return selector === '#brandingForm input[name="__RequestVerificationToken"]' && token
                    ? { value: token } : null;
            },
            addEventListener(name, callback) { if (name === 'DOMContentLoaded') onReady = callback; }
        };
        const window = {
            addEventListener() {}, // lifecycle is tested by the dedicated branding harness
            clearTimeout() {}, setTimeout() {}, confirm: () => true,
            location: { assign(url) { calls.push({ redirect: url }); } },
            EduOSSessionTransitionPending: () => false,
            EduOSWorkspaceResponseMatchesSession(response) {
                return response.headers?.get('X-EduOS-Session-Reference') === 'current';
            },
            EduOSVerifySessionContext() { calls.push({ shield: true }); }
        };
        const fetch = async (url, options = {}) => {
            calls.push({ url, options });
            if (url === '/api/tenant-profile')
                return { ok: true, json: async () => ({ success: true, data: { rowVersion: 'v1' } }) };
            if (url.startsWith('/api/tenant-settings?'))
                return { ok: true, json: async () => ({ success: true, data: { items: [] } }) };
            if (url === '/api/onboarding/status')
                return { ok: true, json: async () => ({ success: true, data: { isComplete: true, currentStage: 8 } }) };
            if (url === '/api/workspace-preferences')
                return {
                    ok: true,
                    headers: { get: key => key === 'X-EduOS-Session-Reference'
                        ? (sessionMatches ? 'current' : 'other') : null },
                    json: async () => ({ success: true, data: {} })
                };
            return { ok: true, json: async () => ({ success: true, data: { id: 1, rowVersion: 'v2' } }) };
        };
        vm.runInNewContext(script, { document, window, fetch, FormData, URL });
        await onReady();
        if (invalid) el('secondaryColorText').value = 'url(bad)';
        await form.listeners.submit({ preventDefault() {} });
        return { calls, alert };
    }
    const absent = await scenario('');
    assert.equal(absent.calls.filter(x => x.options?.method === 'POST').length, 0);
    assert.match(absent.alert.className, /alert-danger/);
    const invalid = await scenario('token-1', true);
    assert.equal(invalid.calls.filter(x => x.options?.method === 'POST').length, 0);
    const switched = await scenario('token-1', false, false);
    assert.equal(switched.calls.filter(x => x.options?.method === 'POST').length, 0);
    assert.ok(switched.calls.some(x => x.shield === true));
    const valid = await scenario('token-1');
    assert.ok(valid.calls.filter(x => x.url === '/api/workspace-preferences').length >= 5);
    const writes = valid.calls.filter(x => x.options?.method === 'POST');
    assert.equal(writes.length, 3);
    assert.ok(writes.every(x => x.options.headers.RequestVerificationToken === 'token-1'));
    assert.ok(valid.calls.some(x => x.redirect === '/Dashboard/Index'));
});

test('student final-exit page bilingual labels and reviewed manual preserve API/security contracts', () => {
    const view = read('EduOS.App/Views/StudentLifecycle/Index.cshtml');
    const client = read('EduOS.App/wwwroot/js/students/lifecycle.js');
    const page = read('EduOS.App/Controllers/StudentLifecycleController.cs');
    const api = read('EduOS.App/Controllers/Api/StudentExitController.cs');
    const service = read('EduOS.Service/Services/Students/StudentExitService.cs');
    const { statusLabel, localizedError } = require('../../EduOS.App/wwwroot/js/students/lifecycle.js');
    assert.ok(page.includes('[Authorize(Roles = "TenantAdmin")]'));
    assert.ok(page.includes('[RequireModule("STUDENT")]'));
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.ok(view.includes('data-bn="@bn.ToString().ToLowerInvariant()"'));
    for (const id of ['lifecycleStudentSearch', 'lifecycleExitType', 'lifecycleReason', 'lifecycleSubmit', 'lifecycleHistory'])
        assert.ok(view.includes('id="' + id + '"'));
    for (const type of ['Transfer', 'Completed', 'Dropout']) assert.ok(view.includes('value="' + type + '"'));
    assert.match(client, /RequestVerificationToken: token/);
    assert.ok(client.includes('clientRequestId: requestId()'));
    assert.match(service, /Outstanding fees must be settled/);
    assert.equal(statusLabel('Active', true), 'সক্রিয়');
    assert.equal(statusLabel('TC', true), 'স্থানান্তরিত');
    assert.equal(statusLabel('Active', false), 'Active');
    assert.equal(localizedError('Outstanding fees must be settled.', 409, true), 'আগে সব বকেয়া ফি পরিশোধ করুন।');
    assert.equal(localizedError('Unexpected error', 409, true), 'তথ্যে সংঘাত হয়েছে। বর্তমান অবস্থা যাচাই করে রিলোড করুন।');
    assert.equal(localizedError('Server error', 503, false), 'Server error');
    const en = guideFor('/StudentLifecycle/Index', 'en-BD');
    const bn = guideFor('/StudentLifecycle/Index', 'bn-BD');
    assert.equal(en.steps.length, 5);
    assert.equal(bn.steps.length, 5);
    assert.match(en.rules, /Dropout can record an outstanding due/);
    assert.match(bn.rules, /বকেয়া/);
    assert.deepEqual(en.related.map(x => x.path), ['/Students/Index', '/StudentTransferRequests/Index']);
});

test('shared Page Information fails closed on tenant/session transitions and awaits verifier', () => {
    assert.equal(helpContextStatus('22','a','22','a',false,false), 'ready');
    assert.equal(helpContextStatus('22','a','23','a',false,false), 'changed');
    assert.equal(helpContextStatus('22','a','22','b',false,false), 'changed');
    assert.equal(helpContextStatus('22','a','22','a',true,false), 'changed');
    assert.equal(helpContextStatus('22','a','22','a',false,true), 'verifying');
    assert.match(js, /await window\.EduOSVerifySessionContext\(\)/);
    assert.match(js, /if \(session\) \{/);
    assert.match(js, /if \(!currentContext\(\)\) return;/);
    assert.match(js, /sessionObserver\.observe\(document\.body/);
    assert.match(js, /sessionObserver\.observe\(shield/);
    assert.match(js, /languageObserver\.observe\(document\.documentElement/);
    assert.match(js, /if \(dialog\.open\) render\(\)/);
    assert.match(js, /if \(state === 'changed'\)/);
    assert.doesNotMatch(js, /innerHTML|eval\(|fetch\(|localStorage/);
});
