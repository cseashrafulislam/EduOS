'use strict';
// Static UX source contracts, not browser/SQL/production Final QA.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const ownership = JSON.parse(read('docs/ux/UX_ROUTE_OWNERSHIP_20261114.json'));
const lanes = ownership.lanes.filter(l => l.id === 'A' || l.id === 'B');
const sourcePath = route => {
    const routePath = route.split('#')[0];
    if (routePath === '/offline-study.html') return 'EduOS.App/wwwroot/offline-study.html';
    let [controller, action] = routePath.slice(1).split('/');
    if (controller === 'InstitutionManagement') action = 'Index';
    if (controller === 'AcademicManagement')
        action = action === 'Prerequisites' ? 'Prerequisites' : action === 'Sections' ? 'SectionRegister' : 'Index';
    if (controller === 'Admissions') {
        if (['Forms', 'NewApplication', 'Applications'].includes(action)) action = 'Index';
        if (['AssessmentSetup', 'Merit'].includes(action)) action = 'Assessments';
    }
    if (controller === 'Attendance') action = 'Index';
    return 'EduOS.App/Views/' + controller + '/' + action + '.cshtml';
};
const routes = id => lanes.find(l => l.id === id).routes.map(r => r.path);
const unique = id => [...new Set(routes(id).map(sourcePath))];
test('UX A/B audit covers all 83 assigned sidebar routes and canonical sources', () => {
    assert.equal(routes('A').length, 41);
    assert.equal(routes('B').length, 42);
    assert.equal(unique('A').length, 28);
    assert.equal(unique('B').length, 37);
    for (const file of [...unique('A'), ...unique('B')])
        assert.ok(fs.existsSync(path.join(root, file)), file + ' missing');
});
test('shared shell supplies title; no redundant page title card', () => {
    for (const file of [...unique('A'), ...unique('B')].filter(f => f.endsWith('.cshtml'))) {
        const html = read(file);
        assert.doesNotMatch(html, /<h1\b/i, file + ': duplicate shell title');
        assert.doesNotMatch(html, /content-header-card/i, file + ': redundant title card');
    }
});
test('UX B has no top-only Info button or blank header toolbar row', () => {
    for (const file of unique('B').filter(f => f.endsWith('.cshtml')))
        assert.doesNotMatch(read(file), /<header\b/i, file + ': separate page header');
});
test('Bootstrap grid rows cannot also be card or card-body containers', () => {
    for (const file of [...unique('A'), ...unique('B')].filter(f => f.endsWith('.cshtml'))) {
        const html = read(file);
        for (const match of html.matchAll(/class="([^"]+)"/g)) {
            const classes = match[1].split(/\s+/);
            if (!classes.includes('row')) continue;
            assert.ok(!classes.some(c => ['card', 'card-body', 'content-card'].includes(c)),
                file + ': ' + match[0] + ' mixes a card with a grid row');
        }
    }
});
test('Library filters are balanced horizontal grids, never tall empty columns', () => {
    for (const [file, form] of [
        ['Integrity', 'libraryIntegrityFilters'], ['Conditions', 'conditionFilters'],
        ['Overdue', 'libraryOverdueFilters'], ['Circulation', 'circulationFilters']
    ]) {
        const source = read('EduOS.App/Views/Library/' + file + '.cshtml');
        const marker = '<form id="' + form + '" class="card mb-2"><div class="card-body"><div class="row g-2 align-items-end">';
        assert.ok(source.includes(marker), file + ': no contained filter grid');
        assert.doesNotMatch(source, /<header\b/i);
        if (file !== 'Circulation') {
            assert.match(source, /class="col-lg-2 col-md-3 d-flex align-items-end"><button/,
                file + ': Apply column must be compact');
            assert.match(source, /class="col-lg-4 col-md-9 d-flex align-items-end flex-wrap gap-2"><button/,
                file + ': secondary actions need a wide flexible column');
        }
    }
});
test('Merit workspace omits empty table, buttons, preview and oversized section headings', () => {
    const view = read('EduOS.App/Views/Admissions/Assessments.cshtml');
    const js = read('EduOS.App/wwwroot/js/admissions/assessments.js');
    const merit = read('EduOS.App/wwwroot/js/admissions/assessments-merit.js');
    for (const id of ['marksTitle', 'meritTitle', 'createAssessmentTitle'])
        assert.ok(view.includes('<h3 id="' + id + '" class="h6 mb-0">'));
    assert.match(view, /id="assessmentMeritPanel" class="content-card d-none"/);
    assert.match(view, /id="assessmentRosterArea" class="d-none"/);
    assert.match(view, /id="assessmentRosterEmpty"/);
    assert.match(js, /assessmentRosterArea.+classList\.toggle\('d-none', !state\.test\)/);
    assert.match(merit, /assessmentMeritPanel.+classList\.remove\('d-none'\)/);
});
test('important actions move into their content cards, not deleted', () => {
    const checks = {
        'EduOS.App/Views/Students/Index.cshtml': ['id="studentFilters"', '/Students/Create'],
        'EduOS.App/Views/AcademicCalendar/Index.cshtml': ['id="calendarNewEvent"', 'id="calendarExportEvents"'],
        'EduOS.App/Views/Attendance/Index.cshtml': ['id="returnToAttendanceReports"', 'id="attendanceReportTitle"'],
        'EduOS.App/Views/Library/Index.cshtml': ['id="librarySearch"', 'id="printLibrary"', 'More library actions'],
        'EduOS.App/Views/LearnerIdentities/Index.cshtml': ['id="identityFormTitle"', 'Access History']
    };
    for (const [file, terms] of Object.entries(checks)) {
        const html = read(file);
        for (const term of terms) assert.ok(html.includes(term), file + ': missing ' + term);
    }
});
test('All UX B aria-labelledby links reference a real element after header removal', () => {
    for (const file of unique('B').filter(f => f.endsWith('.cshtml'))) {
        const html = read(file);
        for (const match of html.matchAll(/aria-labelledby="([^"]+)"/g))
            assert.ok(html.includes('id="' + match[1] + '"'), file + ': orphaned label ' + match[1]);
    }
});
test('Library campus scope uses a server-authorized name selector, never a typed internal ID', () => {
    const controller = read('EduOS.App/Controllers/Api/LibraryController.cs');
    const service = read('EduOS.Service/Services/Library/LibraryService.cs');
    const js = read('EduOS.App/wwwroot/js/library/report-campus-filter.js');
    assert.match(controller, /HttpGet\("report-campus-options"\)/);
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,Principal,Librarian"\)/);
    assert.match(service, /GetReportCampusOptionsAsync/);
    assert.match(service, /_user\.TenantId/);
    assert.match(service, /AllowedStaffCampusIds/);
    assert.match(service, /allowed\.Contains\(x\.Id\)/);
    assert.match(js, /new Option\(campus\.name, String\(campus\.id\)\)/);
    assert.match(js, /EduOSSessionTransitionPending/);
    for (const [file, id] of [
        ['Integrity', 'libraryIntegrityCampus'],
        ['Overdue', 'libraryOverdueCampus'],
        ['Circulation', 'circulationCampus']
    ]) {
        const view = read('EduOS.App/Views/Library/' + file + '.cshtml');
        assert.ok(view.includes('<select id="' + id + '" class="form-select" disabled'));
        assert.doesNotMatch(view, new RegExp('<input id="' + id + '"'));
        assert.ok(view.includes('/js/library/report-campus-filter.js'));
    }
});
