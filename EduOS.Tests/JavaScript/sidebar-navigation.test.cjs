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
        ['academics', '/StudentPortal/Index#learnerConsents', 'portal'],
        ['academics', '/LearnerIdentities/AccessHistory', 'learners'],
        ['academics', '/Learning/CourseCompletions', 'learning'],
        ['academics', '/AssessmentAdministration/Certificates', 'assessments'],
        ['management', '/FeeHeadAccounting/Index', 'finance'],
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
    assert.equal(categoryFor('management', 'finance', '/FeeDueCalendar/Index'), 'reports');
    assert.equal(categoryFor('academics', 'assessments', '/AssessmentAdministration/GradeSchemes'), 'settings');
    assert.equal(categoryFor('academics', 'assessments', '/AssessmentAdministration/Certificates'), 'reports');
    assert.equal(categoryFor('management', 'people', '/PayrollOperations/Index'), 'operations');
    assert.equal(categoryFor('management', 'people', '/HR/Directory'), 'operations');
    assert.equal(categoryFor('management', 'people', '/HR/Leaves'), 'operations');
    assert.equal(categoryFor('management', 'people', '/HR/ShiftAssignments'), 'settings');
    assert.equal(categoryFor('management', 'people', '/PayrollSalaryStructures/Index'), 'settings');
    assert.equal(categoryFor('management', 'people', '/PayrollSalaryStatement/Index'), 'reports');
    assert.equal(categoryFor('management', 'people', '/PayrollPaymentPosting/Index'), 'operations');
    assert.equal(categoryFor('management', 'communication', '/CustomFields/Index'), 'settings');
    assert.equal(categoryFor('management', 'finance', '/AccountingSetup/Index'), 'settings');
    assert.equal(normalizedPath('/Attendance/Index/?q=1'), '/attendance/index');
});

test('shared sidebar leaf labels follow the global Bengali/English locale without adding privileged links', () => {
    for (const [href, expected] of [
        ['/InstitutionManagement/Index', 'প্রতিষ্ঠান ও ক্যাম্পাস'],
        ['/StudentPortal/Index', 'শিক্ষার্থী ও অভিভাবক পোর্টাল'],
        ['/FeeReceivablesAging/Index', 'ফি বকেয়ার প্রতিবেদন'],
        ['/AssessmentAdministration/Certificates', 'সনদ'],
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
    assert.match(layout, /@if \(User\?\.IsInRole\("TenantAdmin"\) == true\)\s*\{\s*<a class="nav-link @ActiveFor\("\/InstitutionManagement"\)/);
    assert.match(layout, /@foreach \(var academicLink in new\[\] \{/);
    assert.doesNotMatch(layout, /href="\/Account\/(InstitutionProfile|CampusSetup|AcademicSetup|ModuleSetup)"/);
    assert.doesNotMatch(layout, /href="\/StudentPortal\/Index#learnerConsents"/);
    assert.match(layout, /@if \(User\?\.IsInRole\("TenantAdmin"\) == true \|\| User\?\.IsInRole\("AdmissionOfficer"\) == true\)\s*\{\s*<a class="nav-link @ActiveFor\("\/Admissions"\)/);
    assert.match(layout, /@if \(User\?\.IsInRole\("TenantAdmin"\) == true \|\| User\?\.IsInRole\("Principal"\) == true \|\|\s*User\?\.IsInRole\("VicePrincipal"\) == true \|\| User\?\.IsInRole\("Teacher"\) == true\)\s*\{\s*<a class="nav-link @ActiveFor\("\/AcademicRoutines"\)/);
    assert.match(script, /sidebar\.scrollTop = state\.scroll/);
    assert.match(script, /selected\?\.closest\('\.sidebar-group'\)/);
    assert.match(script, /aria-current/);
    assert.match(styles, /\.sidebar-group-toggle:focus-visible/);
    assert.match(styles, /\.sidebar-category-toggle:focus-visible/);
    assert.match(styles, /data-workspace-navigation="top"\] \.sidebar \.sidebar-submenu/);
    assert.match(script, /allCategories\.push\(subDetails\)/);
});
