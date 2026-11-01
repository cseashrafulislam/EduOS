const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
const sidebar = require('../../EduOS.App/wwwroot/js/sidebar-navigation.js');
const { guideFor } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');

test('admission three-task workspace retains one existing API/view and enforces Form Setup on the server', () => {
    const controller = read('EduOS.App/Controllers/AdmissionsController.cs');
    const view = read('EduOS.App/Views/Admissions/Index.cshtml');
    const script = read('EduOS.App/wwwroot/js/admissions/index.js');
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,AdmissionOfficer"\)/);
    assert.match(controller, /RequireModule\("ADMISSION"\)/);
    assert.match(controller, /public IActionResult Index\(\) => View\(\);/);
    assert.match(controller, /\[Authorize\(Roles = "TenantAdmin"\)\][\s\S]*?public IActionResult Forms\(\)/);
    for (const [action, value] of [['Forms', 'forms'], ['NewApplication', 'new'], ['Applications', 'applications']]) {
        assert.match(controller, new RegExp('public IActionResult ' + action + '\\(\\)[\\s\\S]*?ViewData\\["AdmissionFeature"\\] = "' + value + '";[\\s\\S]*?return View\\("Index"\\);'));
    }
    for (const id of ['intakeFormList', 'admissionForm', 'admissionFilters', 'applicationList',
        'reviewForm', 'intakeFormEditor', 'enrollmentForm'])
        assert.match(view, new RegExp('id="' + id + '"'));
    assert.match(view, /var showIntake = admissionFeature == "all" \|\| admissionFeature == "forms"/);
    assert.match(view, /var showNew = admissionFeature == "all" \|\| admissionFeature == "new"/);
    assert.match(view, /var showList = admissionFeature == "all" \|\| admissionFeature == "applications"/);
    assert.match(script, /await loadOptions\(\)/);
    assert.doesNotMatch(view, /asp-action="Assessments"/);
});

test('admission assessment setup and marks/merit remain separate views of the original authorized assessment flow', () => {
    const controller = read('EduOS.App/Controllers/AdmissionsController.cs');
    const view = read('EduOS.App/Views/Admissions/Assessments.cshtml');
    assert.match(controller, /public IActionResult Assessments\(\) => View\(\);/);
    for (const [action, feature] of [['AssessmentSetup', 'setup'], ['Merit', 'merit']])
        assert.match(controller, new RegExp('public IActionResult ' + action + '\\(\\)[\\s\\S]*?ViewData\\["AdmissionAssessmentFeature"\\] = "' + feature + '";[\\s\\S]*?return View\\("Assessments"\\);'));
    assert.match(view, /feature == "all" \|\| feature == "setup"/);
    assert.match(view, /feature == "all" \|\| feature == "merit"/);
    for (const id of ['assessmentCreateForm', 'assessmentSelect', 'assessmentRoster',
        'assessmentSave', 'assessmentMerit', 'assessmentPublish', 'assessmentMeritList'])
        assert.ok(view.includes('id="' + id + '"'), id);
    assert.doesNotMatch(view, /asp-controller="Admissions" asp-action="Index"/);
});

test('attendance gives Teachers register only and administrators reports/audit with shared filter dependency', () => {
    const controller = read('EduOS.App/Controllers/AttendanceController.cs');
    const view = read('EduOS.App/Views/Attendance/Index.cshtml');
    const report = read('EduOS.App/wwwroot/js/attendance/correction-report.js');
    const roster = read('EduOS.App/wwwroot/js/attendance/index.js');
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher"\)/);
    assert.match(controller, /RequireModule\("ATTENDANCE"\)/);
    for (const action of ['Reports', 'Corrections'])
        assert.match(controller, new RegExp('\\[Authorize\\(Roles = "TenantAdmin,Principal,VicePrincipal"\\)\\][\\s\\S]*?public IActionResult ' + action + '\\(\\)'));
    assert.match(controller, /public IActionResult Register\(\)/);
    assert.match(view, /var showReports = feature == "all" \|\| feature == "reports"/);
    assert.match(view, /var showCorrections = feature == "all" \|\| feature == "corrections"/);
    assert.match(view, /showReports \|\| showCorrections \? "" : "d-none"/);
    for (const id of ['attendanceReportBatch', 'attendanceReportFrom', 'attendanceReportTo',
        'attendanceCorrectionAuditLoad', 'attendanceCorrectionAuditRows', 'attendanceFilters',
        'returnToAttendanceReports']) assert.match(view, new RegExp('id="' + id + '"'));
    assert.match(report, /el\('attendanceReportBatch'\)/);
    assert.match(report, /el\('attendanceReportFrom'\)/);
    assert.match(roster, /section\[data-attendance-feature="reports"\]/);
    assert.match(roster, /attendance:session-open/);
    assert.match(roster, /returnToAttendanceReports/);
    assert.match(view, /@if \(User\.IsInRole\("TenantAdmin"\) \|\| User\.IsInRole\("Principal"\) \|\| User\.IsInRole\("VicePrincipal"\)\)/);
});

test('institution six destinations reuse tenant-admin services and preserve original tabs on old links', () => {
    const controller = read('EduOS.App/Controllers/InstitutionManagementController.cs');
    const view = read('EduOS.App/Views/InstitutionManagement/Index.cshtml');
    const script = read('EduOS.App/wwwroot/js/institution-management/index.js');
    assert.match(controller, /Authorize\(Roles = "TenantAdmin"\)/);
    for (const [action, feature] of [
        ['Profile', 'profile'], ['Campuses', 'campuses'], ['Regional', 'regional'],
        ['Branding', 'branding'], ['Subdomain', 'subdomain'], ['History', 'history']
    ]) assert.match(controller, new RegExp('public IActionResult ' + action + '\\(\\)[\\s\\S]*?ViewData\\["InstitutionFeature"\\] = "' + feature + '";[\\s\\S]*?return View\\("Index"\\);'));
    for (const id of ['institutionDetailsPanel', 'institutionRegionalPanel', 'institutionBrandingPanel',
        'institutionSubdomainPanel', 'institutionHistoryTitle', 'institutionCampusSection'])
        assert.ok(view.includes('id="' + id + '"'));
    assert.match(view, /feature == "all" \? "" : "d-none"/);
    assert.match(script, /dataset\.institutionFeature === 'campuses'/);
    assert.match(controller, /public IActionResult Index\(\) => View\(\);/);
});

test('canonical sidebar replaces shortcut forests and correctly classifies owner-authorized destinations', () => {
    const students = read('EduOS.App/Views/Students/Index.cshtml');
    for (const href of ['/StudentImports/Index', '/StudentReadmissions/Index', '/StudentProgressions/Index',
        '/Admissions/Forms', '/Admissions/NewApplication', '/Admissions/Applications',
        '/Admissions/AssessmentSetup', '/Admissions/Merit', '/Admissions/Offers', '/Attendance/Register',
        '/Attendance/Reports', '/Attendance/Corrections', '/InstitutionManagement/History'])
        assert.ok(layout.includes(href), href);
    assert.doesNotMatch(layout, /href="\/(Admissions|Attendance|InstitutionManagement)\/Index"/);
    assert.doesNotMatch(students, /asp-controller="(StudentImports|StudentReadmissions|StudentProgressions|EnrollmentManagement)"/);
    assert.equal(sidebar.categoryFor('academics', 'learners', '/Admissions/Forms'), 'settings');
    assert.equal(sidebar.categoryFor('academics', 'learners', '/Admissions/AssessmentSetup'), 'settings');
    assert.equal(sidebar.categoryFor('academics', 'learners', '/Admissions/Merit'), 'operations');
    assert.equal(sidebar.categoryFor('academics', 'learners', '/Admissions/Offers'), 'operations');
    assert.equal(sidebar.categoryFor('academics', 'operations', '/Attendance/Reports'), 'reports');
    assert.equal(sidebar.categoryFor('academics', 'operations', '/Attendance/Corrections'), 'reports');
    assert.equal(sidebar.categoryFor('setup', 'institution', '/InstitutionManagement/History'), 'reports');
    assert.equal(sidebar.categoryFor('setup', 'institution', '/InstitutionManagement/Branding'), 'settings');
    assert.equal(sidebar.groupFor('setup', '/StudentImports/Index'), 'enrollment');
    assert.equal(sidebar.navigationLabelFor('/Attendance/Corrections', 'bn-BD'), 'উপস্থিতি সংশোধনের নিরীক্ষা');
});

test('all fifteen new focused workspaces have reviewed bilingual role, steps, recovery and examples', () => {
    for (const route of [
        ...['Forms','NewApplication','Applications','AssessmentSetup','Merit','Offers'].map(x => '/Admissions/' + x),
        ...['Register','Reports','Corrections'].map(x => '/Attendance/' + x),
        ...['Profile','Campuses','Regional','Branding','Subdomain','History'].map(x => '/InstitutionManagement/' + x)
    ]) {
        const en = guideFor(route, 'en-BD');
        const bn = guideFor(route, 'bn-BD');
        assert.ok(en && bn, route);
        assert.ok(en.steps.length >= 3, route);
        assert.equal(en.steps.length, bn.steps.length);
        assert.ok(en.example && bn.example && en.rules && bn.rules && en.recovery && bn.recovery);
        assert.notEqual(en.purpose, bn.purpose);
    }
});
