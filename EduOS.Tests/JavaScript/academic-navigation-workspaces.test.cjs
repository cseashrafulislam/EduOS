const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const controller = read('EduOS.App/Controllers/AcademicManagementController.cs');
const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
const view = read('EduOS.App/Views/AcademicManagement/Index.cshtml');
const management = read('EduOS.App/wwwroot/js/academic/management.js');
const { guideFor } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');
const { groupFor, categoryFor, scoreActive } = require('../../EduOS.App/wwwroot/js/sidebar-navigation.js');
const features = {
    Departments: 'departments', Years: 'years', Terms: 'terms', Classes: 'classes',
    Sections: 'sections', Rooms: 'rooms', Subjects: 'subjects',
    Curricula: 'curricula', Offerings: 'offerings'
};

test('nine academic routes have one visible editor each, not an in-page tab forest', () => {
    assert.match(controller, /\[Authorize\(Roles = "TenantAdmin"\)\]/);
    assert.match(controller, /Index\(\) => RedirectToAction\(nameof\(Years\)\)/);
    assert.match(controller, /return View\("Index"\)/);
    assert.doesNotMatch(view, /data-panel=|role="tablist"/);
    assert.doesNotMatch(management, /querySelectorAll\('button\[data-panel\]'\)/);
    for (const [action, section] of Object.entries(features)) {
        assert.match(controller, new RegExp(`IActionResult ${action}\\(\\) => Feature\\("${section}"\\)`));
        assert.ok(view.includes(`data-section="${section}"`), `missing ${action} editor`);
        assert.ok(view.includes(`section == "${section}"`), `editor ${action} should be hidden on other routes`);
        assert.match(layout, new RegExp(`Action: "${action}"`));
        const url = '/AcademicManagement/' + action;
        assert.equal(groupFor('setup', url), 'academic-setup');
        assert.equal(categoryFor('setup', 'academic-setup', url), action === 'Offerings' ? 'operations' : 'settings');
        assert.ok(scoreActive(url, url) > 9000);
    }
});

test('onboarding wizard remains callable, but never duplicates canonical sidebar destinations', () => {
    for (const route of ['InstitutionProfile', 'CampusSetup', 'AcademicSetup', 'ModuleSetup']) {
        assert.doesNotMatch(layout, new RegExp(`href="/Account/${route}"`));
        assert.match(read(`EduOS.App/Views/Account/${route}.cshtml`), /_OnboardingLayout/);
    }
    assert.match(layout, /href="@academicPath"/);
    assert.match(layout, /User\?\.IsInRole\("TenantAdmin"\)/);
    assert.doesNotMatch(layout, /href="\/StudentPortal\/Index#learnerConsents"/);
    assert.match(read('EduOS.App/Views/StudentPortal/Index.cshtml'), /href="#learnerConsents"/);
});

test('the already separate curriculum-prerequisites page is directly discoverable', () => {
    assert.match(controller, /IActionResult Prerequisites\(\) => View\(\)/);
    assert.match(layout, /Action: "Prerequisites"/);
    assert.equal(categoryFor('setup', 'academic-setup', '/AcademicManagement/Prerequisites'), 'settings');
    const prerequisiteView = read('EduOS.App/Views/AcademicManagement/Prerequisites.cshtml');
    assert.match(prerequisiteView, /asp-action="Curricula"/);
    assert.ok(guideFor('/AcademicManagement/Prerequisites', 'bn-BD'));
});

test('academic page manuals are complete in English and Bangla for every canonical route', () => {
    for (const action of [...Object.keys(features), 'Prerequisites']) {
        const route = '/AcademicManagement/' + action;
        const en = guideFor(route, 'en-BD');
        const bn = guideFor(route, 'bn-BD');
        assert.ok(en && bn, route);
        assert.equal(en.steps.length, bn.steps.length);
        assert.ok(en.steps.length >= 3);
        assert.notEqual(en.title, bn.title);
        assert.ok(en.example && bn.example && en.rules && bn.rules && en.recovery && bn.recovery);
    }
});

test('theme studio already has direct custom sidebar/header color picking and preserves presets', () => {
    const studio = read('EduOS.App/Views/WorkspacePreferences/Index.cshtml');
    for (const token of ['sidebar', 'header', 'page', 'card', 'button', 'form', 'table'])
        assert.match(studio, new RegExp(`Key: "${token}"`));
    assert.match(studio, /data-custom-color-picker/);
    assert.match(studio, /data-custom-color-text/);
    assert.match(studio, /id="workspacePalette"/);
    assert.match(studio, /id="workspaceDiscard"/);
});
