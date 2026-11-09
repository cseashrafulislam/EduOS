const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const layout = fs.readFileSync(path.join(root, 'EduOS.App/Views/Shared/_Layout.cshtml'), 'utf8');
const styles = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/css/site.css'), 'utf8');
const script = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/sidebar-navigation.js'), 'utf8');
const { groupFor, categoryFor, navigationLabelFor, scoreActive, normalizedPath } = require('../../EduOS.App/wwwroot/js/sidebar-navigation.js');

test('every sidebar section is grouped without replacing role and module gated Razor links', () => {
    for (const section of ['platform', 'setup', 'academics', 'management']) {
        assert.match(layout, new RegExp(`data-sidebar-section="${section}"`));
    }
    assert.match(layout, /sidebar-navigation\.js" asp-append-version="true"/);
    assert.match(layout, /OptionalModuleNavigation\.GetAvailableAsync/);
    assert.match(layout, /V2RolePortalNavigation\.GetLinks/);
    assert.match(script, /nav\.replaceChildren\(container\)/);
    assert.doesNotMatch(script, /innerHTML|eval\(|localStorage/);
});

test('section-specific categories include dynamic role portals and privileged administration', () => {
    const cases = [
        ['platform', '/PlatformInstitutionModulePresets/Index', 'features'],
        ['platform', '/Account/PaymentVerification', 'subscriptions'],
        ['setup', '/TenantPermissionOverrides/Index', 'access'],
        ['setup', '/AcademicManagement/Years', 'academic-setup'],
        ['setup', '/AcademicManagement/Offerings', 'academic-setup'],
        ['setup', '/StudentTransferRequests/Index#destinationInbox', 'enrollment'],
        ['setup', '/CentralWorkCenter/Index', 'work-center'],
        ['academics', '/StudentPortal/Index#learnerConsents', 'portal'],
        ['academics', '/LearnerIdentities/AccessHistory', 'learners'],
        ['academics', '/StudentSupport/Scholarships', 'learners'],
        ['academics', '/StudentSupport/Counseling', 'learners'],
        ['academics', '/Learning/CourseCompletions', 'learning'],
        ['academics', '/CareerEmployers/Index', 'career'],
        ['academics', '/CareerOpportunities/Index', 'career'],
        ['academics', '/CareerPlacements/Index', 'career'],
        ['management', '/FacilityWorkOrders/Index', 'campus'],
        ['management', '/FacilitySpaces/Index', 'campus'],
        ['management', '/FacilityCosts/Index', 'campus'],
        ['academics', '/AssessmentAdministration/Certificates', 'assessments'],
        ['management', '/FeeHeadAccounting/Index', 'finance'],
        ['management', '/InstitutionBudgets/Index', 'finance'],
        ['management', '/AccountingJournals/Index', 'finance'],
        ['management', '/hostel/my-history/print', 'campus'],
        ['management', '/TransportOperations/Family', 'campus'],
        ['management', '/Library/Digital', 'library'],
        ['management', '/CommunicationWorkspace/Index', 'communication'],
        ['management', '/EmployeePortal/Index', 'people']
    ];
    for (const [section, href, expected] of cases) {
        assert.equal(groupFor(section, href), expected, `${section}: ${href}`);
    }
    assert.equal(groupFor('management', '/NewFeature/Index'), 'other');
    assert.equal(groupFor('setup', '/Account/AcademicSetup'), 'other');
    assert.equal(categoryFor('setup', 'academic-setup', '/AcademicManagement/Years'), 'settings');
    assert.equal(categoryFor('setup', 'academic-setup', '/AcademicManagement/Offerings'), 'operations');
    assert.equal(categoryFor('management', 'finance', '/FeeReceivablesAging/Index'), 'reports');
    assert.equal(categoryFor('management', 'finance', '/FeeOperations/Index'), 'operations');
    assert.equal(categoryFor('management', 'finance', '/FeeOperations/Structures'), 'settings');
    assert.equal(categoryFor('management', 'finance', '/FeeOperations/Billing'), 'operations');
    assert.equal(categoryFor('management', 'finance', '/FeeOperations/Ledger'), 'operations');
    assert.equal(categoryFor('management', 'finance', '/FinanceAdjustments/Policies'), 'settings');
    assert.equal(categoryFor('management', 'finance', '/FinanceAdjustments/StudentAdjustments'), 'operations');
    assert.equal(categoryFor('management', 'finance', '/FinanceAdjustments/FineRegister'), 'reports');
    assert.equal(categoryFor('management', 'finance', '/FinanceAdjustments/Refunds'), 'operations');
    assert.equal(categoryFor('management', 'finance', '/FeeDueCalendar/Index'), 'reports');
    assert.equal(categoryFor('academics', 'assessments', '/AssessmentAdministration/GradeSchemes'), 'settings');
    assert.equal(categoryFor('academics', 'assessments', '/AssessmentAdministration/Certificates'), 'reports');
    assert.equal(categoryFor('academics', 'assessments', '/AssessmentAdministration/Marks'), 'operations');
    assert.equal(categoryFor('academics', 'assessments', '/AssessmentAdministration/Results'), 'operations');
    assert.equal(categoryFor('academics', 'assessments', '/AssessmentAdministration/Publications'), 'reports');
    assert.equal(categoryFor('academics', 'assessments', '/AssessmentAdministration/Setup'), 'settings');
    assert.equal(categoryFor('academics', 'assessments', '/AssessmentAdministration/AdmitCards'), 'operations');
    assert.equal(categoryFor('management', 'people', '/PayrollOperations/Index'), 'operations');
    assert.equal(categoryFor('management', 'people', '/HR/Directory'), 'operations');
    assert.equal(categoryFor('management', 'people', '/HR/Leaves'), 'operations');
    assert.equal(categoryFor('management', 'people', '/HR/ShiftAssignments'), 'settings');
    assert.equal(categoryFor('management', 'people', '/PayrollSalaryStructures/Index'), 'settings');
    assert.equal(categoryFor('management', 'people', '/PayrollSalaryStatement/Index'), 'reports');
    assert.equal(categoryFor('management', 'people', '/PayrollPaymentPosting/Index'), 'operations');
    assert.equal(categoryFor('management', 'communication', '/CustomFields/Index'), 'settings');
    assert.equal(categoryFor('management', 'finance', '/AccountingSetup/Index'), 'settings');
    assert.equal(categoryFor('academics', 'career', '/CareerEmployers/Index'), 'settings');
    assert.equal(categoryFor('academics', 'career', '/CareerOpportunities/Index'), 'operations');
    assert.equal(categoryFor('academics', 'career', '/CareerPlacements/Index'), 'operations');
    assert.equal(categoryFor('management', 'campus', '/FacilitySpaces/Index'), 'settings');
    assert.equal(categoryFor('management', 'campus', '/FacilityWorkOrders/Index'), 'operations');
    assert.equal(categoryFor('management', 'campus', '/FacilityCosts/Index'), 'reports');
    assert.equal(categoryFor('management', 'finance', '/InstitutionBudgets/Index'), 'operations');
    assert.equal(categoryFor('academics', 'learners', '/StudentSupport/Counseling'), 'operations');
    assert.equal(categoryFor('academics', 'assessments', '/AssessmentAdministration/Omr'), 'operations');
    assert.equal(normalizedPath('/Attendance/Index/?q=1'), '/attendance/index');
});

test('shared sidebar leaf labels follow the global Bengali/English locale without adding privileged links', () => {
    for (const [href, expected] of [
        ['/InstitutionManagement/Index', 'প্রতিষ্ঠান ও ক্যাম্পাস'],
        ['/StudentPortal/Index', 'শিক্ষার্থী ও অভিভাবক পোর্টাল'],
        ['/FeeReceivablesAging/Index', 'ফি বকেয়ার প্রতিবেদন'],
        ['/AssessmentAdministration/Certificates', 'সনদ'],
        ['/AssessmentAdministration/Results', 'ফলাফল যাচাই ও প্রকাশ'],
        ['/TransportOperations/Index', 'পরিবহন পরিচালনা'],
        ['/HR/Leaves', 'কর্মীর ছুটির আবেদন']
    ]) {
        assert.equal(navigationLabelFor(href, 'bn-BD'), expected);
        assert.equal(navigationLabelFor(href, 'en-BD'), null);
    }
    assert.equal(navigationLabelFor('/PrivateUnlisted/Index', 'bn-BD'), null);
    assert.match(script, /const translated = navigationLabelFor\(link\.getAttribute\('href'\)/);
    assert.doesNotMatch(script, /innerHTML|eval\(|localStorage/);
});

test('active page selection matches full segments and prefers exact child or hash links', () => {
    assert.ok(scoreActive('/AssessmentAdministration/ComponentMarks', '/AssessmentAdministration/ComponentMarks') >
        scoreActive('/AssessmentAdministration/Index', '/AssessmentAdministration/ComponentMarks'));
    assert.ok(scoreActive('/StudentPortal/Index#learnerConsents', '/StudentPortal/Index', '#learnerConsents') >
        scoreActive('/StudentPortal/Index', '/StudentPortal/Index', '#learnerConsents'));
    assert.ok(scoreActive('/StudentPortal/Index', '/StudentPortal/Index') >
        scoreActive('/StudentPortal/Index#learnerConsents', '/StudentPortal/Index'));
    assert.ok(scoreActive('/Learning/Index', '/Learning/CourseCompletions') >= 0);
    assert.equal(scoreActive('/Students/Index', '/StudentDocuments/Index'), -1);
    assert.equal(scoreActive('https://external.example', '/Dashboard/Index'), -1);
});

test('restricted links remain gated in Razor rather than client-side role filtering', () => {
    assert.match(layout, /@if \(User\?\.IsInRole\("TenantAdmin"\) == true\)\s*\{\s*@foreach \(var institutionTask in new\[\] \{/);
    assert.match(layout, /\(Path: "\/InstitutionManagement\/Profile"/);
    assert.match(layout, /\(Path: "\/InstitutionManagement\/Campuses"/);
    assert.match(layout, /@foreach \(var academicLink in new\[\] \{/);
    assert.doesNotMatch(layout, /href="\/Account\/(InstitutionProfile|CampusSetup|AcademicSetup|ModuleSetup)"/);
    assert.doesNotMatch(layout, /href="\/StudentPortal\/Index#learnerConsents"/);
    assert.match(layout, /@if \(User\?\.IsInRole\("TenantAdmin"\) == true \|\| User\?\.IsInRole\("AdmissionOfficer"\) == true\)\s*\{\s*@if \(User\?\.IsInRole\("TenantAdmin"\) == true\)/);
    assert.match(layout, /\(Path: "\/Admissions\/NewApplication"/);
    assert.match(layout, /href="\/Admissions\/Forms"/);
    assert.match(layout, /@if \(User\?\.IsInRole\("TenantAdmin"\) == true \|\| User\?\.IsInRole\("Principal"\) == true \|\|\s*User\?\.IsInRole\("VicePrincipal"\) == true \|\| User\?\.IsInRole\("Teacher"\) == true\)\s*\{\s*<a class="nav-link @ActiveFor\("\/AcademicRoutines"\)/);
    assert.match(script, /sidebar\.scrollTop = state\.scroll/);
    assert.match(script, /selected\?\.closest\('\.sidebar-group'\)/);
    assert.match(script, /aria-current/);
    assert.match(styles, /\.sidebar-group-toggle:focus-visible/);
    assert.match(styles, /\.sidebar-category-toggle:focus-visible/);
    assert.match(styles, /data-workspace-navigation="top"\] \.sidebar \.sidebar-submenu/);
    assert.match(script, /allCategories\.push\(subDetails\)/);
    assert.match(script, /if \(namedCategories\.length <= 1\)/);
    assert.doesNotMatch(script, /sidebar-group-count|sidebar-category-count|'other', 'Other'/);
    assert.doesNotMatch(styles, /\.sidebar-group-count|\.sidebar-category-count/);
});

test('sidebar keyboard and persisted navigation state are scoped to the rendered institution session', () => {
    assert.match(script, /state\.scope !== scope/);
    assert.match(script, /dataset\.authTenantId === initialTenant/);
    assert.match(script, /dataset\.authSessionReference === initialReference/);
    assert.match(script, /if \(!matchesScope\(\)\) return;/);
    assert.match(script, /\['ArrowDown', 'ArrowUp', 'Home', 'End'\]/);
    assert.match(script, /getClientRects\(\)\.length > 0/);
    assert.match(script, /element\.closest\('\[hidden\], \[inert\]'\)/);
    assert.doesNotMatch(script, /localStorage|innerHTML|eval\(/);
});

test('server-rendered static sidebar anchors have no duplicate destinations', () => {
    const sidebar = layout.slice(layout.indexOf('<aside class="sidebar"'), layout.indexOf('</aside>'));
    const links = [...sidebar.matchAll(/<a\b[^>]*href="(\/[^"]+)"/g)]
        .map(match => match[1].toLowerCase());
    assert.ok(links.length > 50, 'route inventory unexpectedly empty');
    assert.equal(new Set(links).size, links.length,
        'duplicate static sidebar routes need explicit review');
});

test('Razor localizes all static sidebar leaf labels before client enhancement', () => {
    const sidebar = layout.slice(layout.indexOf('<aside class="sidebar"'), layout.indexOf('</aside>'));
    const literalEnglish = [...sidebar.matchAll(/<span>([A-Za-z][^<@]*)<\/span>/g)]
        .map(match => match[1].trim()).filter(Boolean);
    const bareAnchorEnglish = [...sidebar.matchAll(/<a\b[^>]*\bhref="\/[^"]+"[^>]*>\s*([A-Za-z][^<@]*)<\/a>/g)]
        .map(match => match[1].trim()).filter(Boolean);
    assert.deepEqual([...literalEnglish, ...bareAnchorEnglish], [],
        'untranslated sidebar spans and bare anchors must not depend on JavaScript');
    for (const [route, en, bn] of [
        ['/LearnerIdentities/AccessHistory', 'Identity Access History', 'পরিচয় তথ্য দেখার ইতিহাস'],
        ['/EmployeeAttendance/Index', 'Employee Attendance', 'কর্মীর উপস্থিতি'],
        ['/LeaveEntitlements/Index', 'Leave Entitlements', 'ছুটির প্রাপ্যতা'],
        ['/FeeDiscountRules/Index', 'Discount Rules', 'ফি ছাড়ের নিয়ম']
    ]) {
        const anchor = [...sidebar.matchAll(/<a\b[^>]*\bhref="(\/[^"]+)"[^>]*>[\s\S]*?<\/a>/g)]
            .find(match => match[1] === route)?.[0];
        assert.ok(anchor, 'missing role-gated navigation: ' + route);
        assert.ok(anchor.includes('currentCulture == "bn-BD" ? "' + bn + '" : "' + en + '"'),
            'first-paint bilingual label missing: ' + route);
    }
    assert.doesNotMatch(sidebar, /"Alumni \/ প্রাক্তন শিক্ষার্থী"/);
    const bilingualLeaves = [...sidebar.matchAll(/<span>@\(currentCulture == "bn-BD" \? "[^"]+" : "[^"]+"\)<\/span>/g)];
    assert.ok(bilingualLeaves.length >= 50, 'bilingual static leaf coverage unexpectedly decreased');
    assert.match(sidebar, /<div class="menu-title">@\(currentCulture == "bn-BD" \? "প্ল্যাটফর্ম প্রশাসন" : "Platform Administration"\)<\/div>/);
    for (const path of ['/PlatformTenants/Index', '/TenantUsers/Index',
        '/FeeReceivablesAging/Index', '/AccountingJournals/Index',
        '/AssessmentAdministration/Certificates', '/AssetLifecycle/Index']) {
        assert.ok(sidebar.includes('href="' + path + '"'), path + ' must remain server-rendered');
    }
    assert.match(layout, /OptionalModuleNavigation\.GetAvailableAsync/);
    assert.match(layout, /V2RolePortalNavigation\.GetLinks/);
});
