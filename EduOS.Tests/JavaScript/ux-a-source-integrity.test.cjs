'use strict';
// Fast UX authoring contracts, not Final QA or browser/SQL acceptance.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const ledger = read('docs/ux/lanes/UX_A.md');
const routes = [...ledger.matchAll(/^- \[[ x]\] \x60(\/[^\x60]+)\x60/gm)].map(x => x[1]);
const viewFor = route => {
    const [controller, action] = route.split('/').filter(Boolean);
    if (controller === 'InstitutionManagement') return 'EduOS.App/Views/InstitutionManagement/Index.cshtml';
    if (controller === 'AcademicManagement') {
        const leaf = action === 'Sections' ? 'SectionRegister' : action === 'Prerequisites' ? 'Prerequisites' : 'Index';
        return 'EduOS.App/Views/AcademicManagement/' + leaf + '.cshtml';
    }
    return 'EduOS.App/Views/' + controller + '/' + action + '.cshtml';
};
test('UX A owns exactly 41 unique sidebar routes with existing canonical views', () => {
    assert.equal(routes.length, 41);
    assert.equal(new Set(routes).size, 41);
    for (const route of routes) {
        const file = viewFor(route);
        assert.ok(fs.existsSync(path.join(root, file)), route + ' is missing view ' + file);
    }
});
test('all UX A pages use the global title and have valid table head structure', () => {
    for (const file of new Set(routes.map(viewFor))) {
        const s = read(file);
        assert.match(s, /ViewData\["Title"\]/, file + ' is missing shell title');
        assert.doesNotMatch(s, /<h1\b/i, file + ' duplicates page title');
        assert.doesNotMatch(s, /<th\s+scope="col"ead>/i, file + ' has invalid table HTML');
        const open = (s.match(/<thead(?:\s[^>]*)?>/g) || []).length;
        const close = (s.match(/<\/thead>/g) || []).length;
        assert.equal(open, close, file + ' has unbalanced table headers');
        const scripts = [...s.matchAll(/<script[^>]*\bsrc=["']~\/([^"']+\.js)["']/g)].map(x => x[1]);
        for (const asset of scripts)
            assert.ok(fs.existsSync(path.join(root, 'EduOS.App/wwwroot', asset)), file + ' missing script ' + asset);
    }
});
test('Institution and Academic sidebar actions remain real authorized routes', () => {
    const institution = read('EduOS.App/Controllers/InstitutionManagementController.cs');
    const academic = read('EduOS.App/Controllers/AcademicManagementController.cs');
    for (const action of ['Profile','Campuses','Regional','Branding','Subdomain','History'])
        assert.match(institution, new RegExp('IActionResult ' + action + '\\('), action);
    for (const action of ['Years','Terms','Departments','Classes','Batches','Sections','Rooms','Subjects','Curricula','Prerequisites','Offerings'])
        assert.match(academic, new RegExp('IActionResult ' + action + '\\('), action);
    assert.match(institution, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(academic, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(read('EduOS.App/Views/InstitutionManagement/Index.cshtml'), /id="campusCode" type="hidden"/);
    assert.match(read('EduOS.App/Views/AcademicManagement/SectionRegister.cshtml'), /id="sectionCode" type="hidden"/);
});
test('academic create forms do not solicit editable system Codes', () => {
    const s = read('EduOS.App/Views/AcademicManagement/Index.cshtml');
    for (const id of ['departmentCode','yearCode','termCode','programCode','levelCode',
        'trackCreateCode','roomCreateCode','batchCreateCode','subjectCode','curriculumCode','offeringCode'])
        assert.match(s, new RegExp('id="' + id + '" type="hidden"'), id);
    assert.match(read('EduOS.Service/Services/Academic/AcademicSetupService.cs'), /EffectiveInternalCode/);
    assert.match(read('EduOS.Service/Services/Academic/AcademicSectionService.cs'), /SEC-/);
    assert.match(read('EduOS.Service/Services/Academic/AcademicSetupService.SubjectOfferings.cs'), /OFR-/);
});
test('contextual bilingual Page Help is available for all 41 UX A routes', () => {
    const source = read('EduOS.App/wwwroot/js/platform/page-context-help.js').toLowerCase();
    for (const route of routes) {
        const key = route === '/Dashboard/Admin' ? '/dashboard/admin/index' : route.toLowerCase() + (
            route.toLowerCase().endsWith('/index') ? '' : '/index');
        assert.ok(source.includes("'" + key + "':"), 'Missing page help: ' + route + ', key: ' + key);
    }
});
test('high-risk settings retain server authorization rather than relying on hidden UI controls', () => {
    const protectedControllers = ['TenantUsers','TenantPermissionOverrides','TenantDomains',
        'TenantTerminology','TenantSettings','SmartSetup','NumberSeries','TenantModules',
        'IntegrationAdministration','SubscriptionBilling','UsageHistory'];
    for (const c of protectedControllers) {
        const s = read('EduOS.App/Controllers/' + c + 'Controller.cs');
        assert.match(s, /\[Authorize\(Roles = "TenantAdmin"\)\]/, c + ' must be tenant-admin only');
    }
    const governance = read('EduOS.App/Controllers/LegalDocumentsController.cs');
    assert.match(governance, /\[Authorize\(Roles = "SuperAdmin"\)\]/);
    const payment = read('EduOS.App/Controllers/SubscriptionPaymentHistoryController.cs');
    assert.match(payment, /\[Authorize\(Roles = "SuperAdmin"\)\]/);
});
