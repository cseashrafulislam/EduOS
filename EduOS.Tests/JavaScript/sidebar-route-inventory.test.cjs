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
const routeSet = new Set([
    ...[...sidebar.matchAll(/<a\b[^>]*\bhref="(\/[^"]+)"/g)].map(m => m[1]),
    ...[...sidebar.matchAll(/\bPath:\s*"(\/[^"]+)"/g)].map(m => m[1]),
    ...[...portals.matchAll(/\bAdd\("[^"]+",\s*"(\/[^"]+)"/g)].map(m => m[1])
]);
function resolve(route) {
    const clean = route.split(/[?#]/)[0].replace(/\/+$/, '');
    const attr = clean.slice(1).toLowerCase();
    if (sources.some(x => [...x.source.matchAll(/\[(?:HttpGet|Route)\s*\(\s*"([^"]+)"/g)]
        .some(m => m[1].toLowerCase() === attr))) return true;
    const [name, action = 'Index'] = clean.split('/').filter(Boolean);
    const controller = conventional.get((name || '').toLowerCase());
    if (!controller) return false;
    return new RegExp('\\b' + action.replace(/[^A-Za-z0-9_]/g, '') + '\\s*\\(')
        .test(controller.source);
}
test('every literal sidebar, tuple and role-portal destination resolves to current MVC source', () => {
    assert.ok(routeSet.size >= 120, 'unexpectedly small route inventory');
    const missing = [...routeSet].filter(route => !resolve(route));
    assert.deepEqual(missing, [], 'broken navigation targets must be repaired, not hidden');
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
