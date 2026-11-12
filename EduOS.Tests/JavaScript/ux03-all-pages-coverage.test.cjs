'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const { audit, policy, combinePolicy } = require('../../scripts/ux03-route-coverage.cjs');
const validation = require('../../EduOS.App/wwwroot/js/platform/native-form-validation.js');

function environment(lang) {
    const listeners = new Map();
    const nodes = new Map();
    const doc = {
        documentElement: { lang },
        getElementById: id => nodes.get(id) || null,
        createElement: () => ({ id: '', className: '', textContent: '',
            setAttribute() {}, remove() { nodes.delete(this.id); } }),
        addEventListener(name, fn) { listeners.set(name, fn); }
    };
    const attrs = new Map();
    const field = {
        form: { noValidate: false, querySelectorAll: () => [] }, name: 'Amount', ownerDocument: doc,
        dataset: {}, validity: { valid: false, valueMissing: true },
        hasAttribute: key => attrs.has(key),
        getAttribute: key => attrs.get(key) || null,
        setAttribute: (key, value) => attrs.set(key, value),
        removeAttribute: key => attrs.delete(key),
        insertAdjacentElement(position, node) { assert.equal(position, 'afterend'); nodes.set(node.id, node); }
    };
    validation.mount(doc);
    return { doc, field, attrs, nodes, listeners };
}

test('all Razor views receive an explicit source-only, non-certified route/role/mobile/bilingual record', () => {
    const result = audit();
    assert.equal(result.totals.views, 296);
    assert.ok(result.totals.rows >= result.totals.views);
    assert.ok(result.totals.MVCActionRows >= 200);
    assert.ok(result.totals.unresolvedViewRows > 0);
    assert.equal(result.totals.authenticatedAcceptancePassed, 0);
    const seen = new Set(result.records.map(row => row.view));
    assert.equal(seen.size, result.totals.views);
    assert.ok(seen.has('EduOS.App/Views/Shared/OfficialEducationBooks.cshtml'),
        'shared official education directory remains in source inventory');
    for (const row of result.records) {
        assert.match(row.status, /SOURCE INVENTORY ONLY/);
        assert.match(row.roleTenant, /NOT VERIFIED/);
        assert.match(row.mobile, /360\/390\/768\/1280/);
        assert.match(row.bilingual, /en-BD and bn-BD/);
        assert.ok(row.view.endsWith('.cshtml'));
        if (row.helpBnReviewed) assert.equal(row.helpEnReviewed, true);
    }
});

test('MVC action-specific roles remain additional to controller and module policies', () => {
    const result = audit();
    const admission = result.records.find(row => row.route === '/Admissions/Offers');
    assert.deepEqual(admission.role.controllerRoles, ['TenantAdmin', 'AdmissionOfficer']);
    assert.deepEqual(admission.role.actionRoles, ['TenantAdmin']);
    assert.deepEqual(admission.role.modules, ['ADMISSION']);
    const combined = combinePolicy(policy('[Authorize(Roles = "TenantAdmin,Principal")][RequireModule("FEES")]'),
        policy('[Authorize(Roles = "Accountant")]'));
    assert.deepEqual(combined.controllerRoles, ['TenantAdmin', 'Principal']);
    assert.deepEqual(combined.actionRoles, ['Accountant']);
    assert.deepEqual(combined.modules, ['FEES']);
});

test('route inventory keeps MVC screens distinct from API names, nested controllers and print endpoints', () => {
    const result = audit();
    assert.equal(result.totals.unresolvedNonPartialRows, 0);
    assert.equal(result.totals.frameworkViewRows, 2);
    assert.equal(result.totals.partialViewRows, 52);
    const academic = result.records.find(row => row.route === '/AcademicInstruction/Index');
    assert.deepEqual(academic.role.controllerRoles, ['TenantAdmin', 'Principal', 'VicePrincipal', 'Teacher']);
    assert.deepEqual(academic.role.modules, ['ACADEMIC']);
    assert.equal(result.records.find(row => row.route === '/Pricing/Index').role.allowAnonymous, true);
    assert.deepEqual(result.records.find(row => row.route === '/WebhookTests/Index').role.controllerRoles,
        ['TenantAdmin']);
    const manifests = result.records.filter(row => row.view.endsWith('/TripManifestPrint.cshtml'));
    assert.deepEqual(manifests.map(row => row.route).sort(), [
        '/transport/driver-trips/{reference:guid}/print',
        '/transport/trips/{reference:guid}/print'
    ]);
    assert.deepEqual(manifests.find(row => row.route.includes('/driver-trips/')).role.actionRoles, ['Staff']);
    assert.deepEqual(manifests.find(row => row.route.includes('/trips/')).role.actionRoles,
        ['TenantAdmin', 'Principal', 'TransportManager']);
});

test('page-level native constraint errors are bilingual, safe and do not block canonical form posting', () => {
    for (const [lang, expected] of [['en-BD', 'Complete this field.'], ['bn-BD', 'এই ঘরটি পূরণ করুন।']]) {
        const e = environment(lang);
        e.listeners.get('invalid')({ target: e.field });
        assert.equal(e.nodes.size, 1);
        const node = [...e.nodes.values()][0];
        assert.equal(node.textContent, expected);
        assert.equal(e.attrs.get('aria-describedby'), node.id);
        assert.equal(e.attrs.get('aria-invalid'), 'true');
        e.field.validity = { valid: true };
        e.listeners.get('input')({ target: e.field });
        assert.equal(e.nodes.size, 0);
        assert.equal(e.attrs.has('aria-invalid'), false);
        assert.equal(e.attrs.has('aria-describedby'), false);
        assert.equal(e.listeners.has('submit'), false);
    }
});

test('native validation keeps existing application errors and accessibility descriptions intact', () => {
    const e = environment('en-BD');
    e.field.setAttribute('aria-describedby', 'business-error');
    e.field.setAttribute('aria-invalid', 'true');
    e.field.form.querySelectorAll = () => [{ getAttribute: () => 'Amount', textContent: 'Balance exceeds limit.' }];
    e.listeners.get('invalid')({ target: e.field });
    assert.equal(e.nodes.size, 0, 'never replace a server/business rule error');
    e.field.form.querySelectorAll = () => [];
    e.listeners.get('invalid')({ target: e.field });
    const ownId = e.field.dataset.eduNativeError;
    assert.equal(e.attrs.get('aria-describedby'), 'business-error ' + ownId);
    e.field.validity = { valid: true };
    e.listeners.get('change')({ target: e.field });
    assert.equal(e.attrs.get('aria-describedby'), 'business-error');
    assert.equal(e.attrs.get('aria-invalid'), 'true', 'do not clear a pre-existing invalid state');
});

test('native validation respects novalidate and explicit opt-out, and is mounted only once', () => {
    const e = environment('bn-BD');
    const initial = e.listeners.get('invalid');
    validation.mount(e.doc);
    assert.equal(e.listeners.get('invalid'), initial);
    e.field.form.noValidate = true;
    initial({ target: e.field });
    assert.equal(e.nodes.size, 0);
    e.field.form.noValidate = false;
    e.field.hasAttribute = key => key === 'data-edu-no-native-feedback';
    initial({ target: e.field });
    assert.equal(e.nodes.size, 0);
});

test('native feedback restores pre-existing aria-invalid=false on correction and never echoes private values', () => {
    const e = environment('bn-BD');
    e.field.setAttribute('aria-invalid', 'false');
    e.field.value = 'secret personal value';
    e.listeners.get('invalid')({ target: e.field });
    assert.equal(e.attrs.get('aria-invalid'), 'true');
    assert.doesNotMatch([...e.nodes.values()][0].textContent, /secret personal value/);
    e.field.validity = { valid: true };
    e.listeners.get('input')({ target: e.field });
    assert.equal(e.attrs.get('aria-invalid'), 'false');
});

test('authenticated and public layouts both load shared non-blocking feedback, and phone rules have safe wrap', () => {
    for (const file of ['EduOS.App/Views/Shared/_Layout.cshtml', 'EduOS.App/Views/Shared/_PublicLayout.cshtml'])
        assert.match(read(file), /<script src="~\/js\/platform\/native-form-validation\.js" asp-append-version="true"><\/script>/);
    const css = read('EduOS.App/wwwroot/css/site.css');
    assert.match(css, /\.page-body form > :is\(\.d-flex,\.btn-group\)/);
    assert.match(css, /\.public-main form > :is\(\.d-flex,\.btn-group\)/);
    assert.match(css, /\.edu-native-validation/);
    assert.doesNotMatch(read('EduOS.App/wwwroot/js/platform/native-form-validation.js'), /fetch\s*\(|XMLHttpRequest|addEventListener\('submit'/);
});
