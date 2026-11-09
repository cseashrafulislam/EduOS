'use strict';
// Static navigation inventory: verifies literal Razor anchors, tuple destinations
// and entitlement-gated role portal links against current controller source.
// Does not grant access, execute a route, or claim authenticated browser proof.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
const portals = read('EduOS.App/Authorization/V2RolePortalNavigation.cs');
const sidebar = layout.slice(layout.indexOf('<aside class="sidebar"'), layout.indexOf('</aside>'));
function controllers(dir) {
    return fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
        const file = path.join(dir, entry.name);
        if (entry.isDirectory()) return controllers(file);
        return entry.name.endsWith('Controller.cs') ? [file] : [];
    });
}
const sources = controllers(path.join(root, 'EduOS.App/Controllers'))
    .map(file => ({ file, source: fs.readFileSync(file, 'utf8') }));
const conventional = new Map(sources.filter(x =>
    path.dirname(x.file) === path.join(root, 'EduOS.App/Controllers'))
    .map(x => [path.basename(x.file, 'Controller.cs').toLowerCase(), x]));
// Include computed Action tuples, not only literal href/Path/role-portal URLs.
// Unknown computed tuple patterns must fail rather than silently evade inventory.
const dynamicRoutes = [];
for (const match of sidebar.matchAll(/@foreach\s*\(var\s+(\w+)\s+in\s+new\[\]\s*\{([\s\S]*?)\}\s*\)/g)) {
    const [, variable, tuples] = match;
    const actions = [...tuples.matchAll(/\bAction:\s*"([^"]+)"/g)].map(m => m[1]);
    if (!actions.length) continue;
    const following = sidebar.slice(match.index + match[0].length, match.index + match[0].length + 1400);
    const prefix = following.match(new RegExp(
        'var\\s+\\w+Path\\s*=\\s*"([^"]+)"\\s*\\+\\s*' + variable + '\\.Action'))?.[1];
    assert.ok(prefix, 'unresolved computed sidebar path for ' + variable);
    dynamicRoutes.push(...actions.map(action => prefix + action));
}
const routeEntries = [
    ...[...sidebar.matchAll(/<a\b[^>]*\bhref="(\/[^"]+)"/g)].map(m => m[1]),
    ...[...sidebar.matchAll(/\bPath:\s*"(\/[^"]+)"/g)].map(m => m[1]),
    ...[...portals.matchAll(/\bAdd\("[^"]+",\s*"(\/[^"]+)"/g)].map(m => m[1]),
    ...dynamicRoutes
];
const routeSet = new Set(routeEntries);
function resolve(route) {
    const clean = route.split(/[?#]/)[0].replace(/\/+$/, '');
    // Public offline reader is a reviewed static entry point, not an MVC action.
    if (/^\/[a-z0-9-]+\.html$/i.test(clean) &&
        fs.existsSync(path.join(root, 'EduOS.App/wwwroot', clean.slice(1)))) return true;
    const attr = clean.slice(1).toLowerCase();
    if (sources.some(x => [...x.source.matchAll(/\[(?:HttpGet|Route)\s*\(\s*"([^"]+)"/g)]
        .some(m => m[1].toLowerCase() === attr))) return true;
    const [name, action = 'Index'] = clean.split('/').filter(Boolean);
    const controller = conventional.get((name || '').toLowerCase());
    if (!controller) return false;
    return new RegExp('\\b' + action.replace(/[^A-Za-z0-9_]/g, '') + '\\s*\\(')
        .test(controller.source);
}
test('literal, computed Action tuple and role-portal destinations resolve to current MVC source', () => {
    assert.ok(dynamicRoutes.length >= 45, 'computed Action tuple coverage regressed');
    assert.ok(routeSet.size >= 175, 'unexpectedly small route inventory');
    const missing = [...routeSet].filter(route => !resolve(route));
    assert.deepEqual(missing, [], 'broken navigation targets must be repaired, not hidden');
});
test('navigation declarations are duplicate-free across literal and computed sources', () => {
    const folded = routeEntries.map(route => route.toLowerCase());
    assert.equal(new Set(folded).size, folded.length,
        'duplicate static, computed or role-portal navigation requires review');
});

test('every current sidebar destination belongs to a named task group, not a generic catch-all', () => {
    const { groupFor } = require('../../EduOS.App/wwwroot/js/sidebar-navigation.js');
    const sections = [...sidebar.matchAll(/<nav\b[^>]*data-sidebar-section="([^"]+)"[^>]*>([\s\S]*?)<\/nav>/g)];
    assert.equal(sections.length, 4, 'all sidebar sections must be inventoried');
    for (const [, section, markup] of sections) {
        const destinations = [
            ...[...markup.matchAll(/<a\b[^>]*\bhref="(\/[^\"]+)"/g)].map(m => m[1]),
            ...[...markup.matchAll(/\bPath:\s*"(\/[^\"]+)"/g)].map(m => m[1])
        ];
        for (const [, match, variable, tuples] of markup.matchAll(/(@foreach\s*\(var\s+(\w+)\s+in\s+new\[\]\s*\{([\s\S]*?)\}\s*\))/g)) {
            if (!/\bAction:\s*"/.test(tuples)) continue;
            const next = markup.slice(markup.indexOf(match) + match.length, markup.indexOf(match) + match.length + 1400);
            const path = next.match(new RegExp('var\\s+\\w+Path\\s*=\\s*"([^"]+)"\\s*\\+\\s*' + variable + '\\.Action'))?.[1];
            assert.ok(path, 'computed navigation prefix missing for ' + variable);
            destinations.push(...[...tuples.matchAll(/\bAction:\s*"([^"]+)"/g)].map(m => path + m[1]));
        }
        assert.ok(destinations.length > 0, 'missing ' + section + ' destinations');
        for (const route of destinations) {
            if (route.toLowerCase().startsWith('/dashboard/')) continue;
            assert.notEqual(groupFor(section, route), 'other', `${section}: ${route}`);
        }
    }
});

test('role portal remains server entitlement/role gated; inventory cannot grant a link', () => {
    assert.match(portals, /availableModules\.Contains\(module\)/);
    assert.match(portals, /roles\.Any\(user\.IsInRole\)/);
    assert.match(layout, /V2RolePortalNavigation\.GetLinks/);
    assert.match(layout, /OptionalModuleNavigation\.GetAvailableAsync/);
});
test('dynamic sidebar task groups include career and facilities without duplicating routes', () => {
    const { groupFor, categoryFor } = require('../../EduOS.App/wwwroot/js/sidebar-navigation.js');
    const expected = [
        ['academics', '/CareerEmployers/Index', 'career', 'settings'],
        ['academics', '/CareerOpportunities/Index', 'career', 'operations'],
        ['academics', '/CareerPlacements/Index', 'career', 'operations'],
        ['management', '/FacilitySpaces/Index', 'campus', 'settings'],
        ['management', '/FacilityWorkOrders/Index', 'campus', 'operations']
    ];
    for (const [section, route, group, category] of expected) {
        assert.equal(groupFor(section, route), group, route);
        assert.equal(categoryFor(section, group, route), category, route);
    }
    assert.equal([...routeSet].filter(x => x === '/FacilitySpaces/Index').length, 1);
    assert.equal([...routeSet].filter(x => x === '/CareerPlacements/Index').length, 1);
});

test('new academic, teacher and Bangladesh study pages have one discoverable sidebar destination', () => {
    const { groupFor, categoryFor } = require('../../EduOS.App/wwwroot/js/sidebar-navigation.js');
    const expected = [
        ['/Accreditation/Obe', 'academics', 'accreditation', 'operations'],
        ['/Learning/StudyPlan', 'academics', 'learning', 'operations'],
        ['/TeacherStudio/Public', 'academics', 'learning', 'operations'],
        ['/TeacherStudio/AdaptiveGoals', 'academics', 'learning', 'operations'],
        ['/TeacherStudio/Cpd', 'academics', 'learning', 'operations'],
        ['/TeacherStudio/CpdReview', 'academics', 'learning', 'operations'],
        ['/Library/Study', 'management', 'library', 'operations'],
        ['/Library/StudyAssistant', 'management', 'library', 'operations'],
        ['/offline-study.html', 'management', 'library', 'operations']
    ];
    for (const [route, section, group, category] of expected) {
        assert.equal(routeEntries.filter(x => x === route).length, 1, route);
        assert.equal(groupFor(section, route), group, route);
        assert.equal(categoryFor(section, group, route), category, route);
    }
});

test('new sidebar links remain inside module and action role gates', () => {
    function precedingGuard(route) {
        const anchor = sidebar.indexOf('href="' + route + '"');
        assert.ok(anchor > 0, route + ' is absent');
        const start = sidebar.lastIndexOf('@if (', anchor);
        return sidebar.slice(start, anchor);
    }
    assert.match(precedingGuard('/Learning/StudyPlan'), /IsInRole\("Student"\)/);
    assert.match(precedingGuard('/TeacherStudio/Cpd'), /IsInRole\("Teacher"\)/);
    assert.doesNotMatch(precedingGuard('/TeacherStudio/Cpd'), /IsInRole\("Principal"\)/);
    assert.match(precedingGuard('/TeacherStudio/CpdReview'), /IsInRole\("TenantAdmin"\)/);
    assert.match(precedingGuard('/TeacherStudio/CpdReview'), /IsInRole\("Principal"\)/);
    assert.doesNotMatch(precedingGuard('/TeacherStudio/CpdReview'), /IsInRole\("Teacher"\)/);
    assert.match(precedingGuard('/Library/StudyAssistant'), /IsInRole\("Student"\)/);
    assert.doesNotMatch(precedingGuard('/Library/StudyAssistant'), /IsInRole\("Librarian"\)/);
    assert.match(precedingGuard('/Library/Study'), /IsInRole\("Librarian"\)/);
    assert.match(precedingGuard('/Library/Study'), /IsInRole\("Guardian"\)/);
    assert.match(precedingGuard('/Accreditation/Obe'), /IsInRole\("AccreditationOfficer"\)/);
    assert.match(sidebar, /@if \(showLms[\s\S]*?href="\/Learning\/StudyPlan"/);
    assert.match(sidebar, /@if \(showLibrary\)[\s\S]*?href="\/Library\/StudyAssistant"/);
});


test('Accounting Reports sidebar gate matches TenantAdmin-only controller', () => {
    const source = read('EduOS.App/Controllers/AccountingReportsController.cs');
    assert.match(source, /Authorize\(Roles\s*=\s*"TenantAdmin"\)/);
    const index = layout.indexOf('href="/AccountingReports/Index"');
    assert(index > 0);
    assert.match(layout.slice(index - 300, index),
        /@if\s*\(User\?\.IsInRole\("TenantAdmin"\)\s*==\s*true\)\s*\{\s*<a\b[\s\S]*$/);
});
test('Facility Spaces excludes FacilitiesRequester while Work Orders retains that role', () => {
    const spaces = read('EduOS.App/Controllers/FacilitySpacesController.cs');
    const orders = read('EduOS.App/Controllers/FacilityWorkOrdersController.cs');
    assert.match(spaces, /Authorize\(Roles\s*=\s*"TenantAdmin,Principal,FacilitiesManager,FacilitiesTechnician,FacilitiesApprover"\)/);
    assert.match(orders, /FacilitiesRequester/);
    const work = layout.indexOf('href="/FacilityWorkOrders/Index"');
    const facility = layout.indexOf('href="/FacilitySpaces/Index"');
    assert(work > 0 && facility > work);
    assert.match(layout.slice(facility - 450, facility),
        /@if\s*\(User\?\.IsInRole\("TenantAdmin"\)[\s\S]*?User\?\.IsInRole\("FacilitiesTechnician"\)\s*==\s*true\)\s*\{\s*<a\b[\s\S]*$/);
});
