'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { validateTarget, isAllowedReadOnlyRequest } =
    require('../../tests/browser/academic-live-guards.cjs');

test('academic live smoke requires an approved canonical HTTPS hostname', () => {
    const base = 'https://staging.eduos.example';
    assert.equal(validateTarget(base, 'staging.eduos.example'), base);
    assert.equal(validateTarget('http://127.0.0.1:5010/', '127.0.0.1'), 'http://127.0.0.1:5010');
    for (const value of [
        'http://staging.eduos.example', 'https://production.eduos.example',
        'https://staging.eduos.example.evil.test', 'https://u:p@staging.eduos.example',
        'https://staging.eduos.example/path', 'https://staging.eduos.example/?redirect=evil',
        'https://staging.eduos.example/#fragment', 'javascript:alert(1)'
    ]) assert.throws(() => validateTarget(value, 'staging.eduos.example'));
    assert.throws(() => validateTarget(base, ''));
    assert.throws(() => validateTarget(base, 'https://staging.eduos.example'));
});

test('academic browser allows only same-origin reads and the exact login POST', () => {
    const base = 'https://staging.eduos.example';
    assert(isAllowedReadOnlyRequest(base + '/AcademicManagement/Index', 'GET', base));
    assert(isAllowedReadOnlyRequest(base + '/api/academic-setup/catalog', 'GET', base));
    assert(isAllowedReadOnlyRequest(base + '/api/auth/login', 'POST', base));
    for (const [url, verb] of [
        [base + '/api/fees/pay', 'POST'],
        [base + '/api/auth/login?next=/dashboard', 'POST'],
        [base + '/api/auth/login/other', 'POST'],
        [base + '/api/auth/login', 'PUT'],
        [base + '/api/auth/login', 'DELETE'],
        [base + '/api/academic-setup/catalog', 'PATCH'],
        ['https://evil.test/collect', 'GET'],
        ['https://staging.eduos.example.evil.test/path', 'GET'],
        ['http://staging.eduos.example/AcademicManagement/Index', 'GET'],
        ['not-a-url', 'GET']
    ]) assert.equal(isAllowedReadOnlyRequest(url, verb, base), false, verb + ' ' + url);
});

test('academic authenticated smoke uses current login identifier and canonical features', () => {
    const root = path.join(__dirname, '../..');
    const script = fs.readFileSync(path.join(root,'tests/browser/academic-live.cjs'),'utf8');
    const login = fs.readFileSync(path.join(root,'EduOS.App/Views/Account/Login.cshtml'),'utf8');
    const controller = fs.readFileSync(path.join(root,
        'EduOS.App/Controllers/AcademicManagementController.cs'),'utf8');
    const view = fs.readFileSync(path.join(root,
        'EduOS.App/Views/AcademicManagement/Index.cshtml'),'utf8');
    assert(login.includes('id="userNameOrEmail"'));
    assert(script.includes("page.locator('#userNameOrEmail').fill(email)"));
    assert(!script.includes("page.locator('#email')"));
    assert(controller.includes('Index() => RedirectToAction(nameof(Years))'));
    assert(script.includes("['Curricula', 'curricula']"));
    assert(script.includes("['Batches', 'sections']"));
    assert(script.includes("'/AcademicManagement/Sections'"));
    assert(view.includes('data-active-feature'));
    assert(!script.includes('button[data-panel]'), 'Removed client-side feature tabs must not be tested');
});

test('workflow and browser enforce exact-SHA, target-host and read-only guard', () => {
    const root = path.join(__dirname, '../..');
    const workflow = fs.readFileSync(path.join(root,
        '.github/workflows/academic-live-browser.yml'), 'utf8');
    const browser = fs.readFileSync(path.join(root, 'tests/browser/academic-live.cjs'), 'utf8');
    for (const check of ['github.ref', 'refs/heads/Ashraful', '$GITHUB_SHA',
        'EDUOS_E2E_EXPECTED_HOST', 'academic-live-guards.test.cjs'])
        assert(workflow.includes(check), 'Missing workflow release safety: ' + check);
    for (const check of ['validateTarget(', 'isAllowedReadOnlyRequest(',
        "context.route('**/*'", "route.abort('blockedbyclient')", 'blockedWrites.length'])
        assert(browser.includes(check), 'Missing browser read-only safety: ' + check);
});
