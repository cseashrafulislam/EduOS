'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js/admissions/index.js'), 'utf8')
    .replace(/\}\)\(\);\s*$/, 'globalThis.__admissionTest = { loadOptions, submitApplication, exportApplicationPage, apiOptions, state, guards };})();');

function setup() {
    const nodes = new Map();
    const ids = ['academicYearId', 'academicTermId', 'campusId', 'academicUnitId',
        'intakeYearId', 'intakeTermId', 'intakeCampusId', 'intakeUnitId', 'admissionFormReference'];
    const node = id => {
        if (!nodes.has(id)) nodes.set(id, {
            id, value: '', textContent: '', dataset: {}, focus() {}, remove() {},
            checkValidity: () => true, querySelectorAll: () => [],
            setAttribute() {}, removeAttribute() {}, addEventListener() {},
            append(...children) { this.children = [...(this.children || []), ...children]; },
            replaceChildren(...children) { this.children = children; },
            reset() { this.resets = (this.resets || 0) + 1; }
        });
        return nodes.get(id);
    };
    for (const id of ids) {
        const select = node(id);
        let value = '';
        select.options = [];
        Object.defineProperty(select, 'value', {
            get() { return value; },
            set(next) { value = select.options.some(item => item.value === String(next)) ? String(next) : ''; }
        });
        Object.defineProperty(select, 'selectedIndex', {
            set(index) { value = select.options[index]?.value || ''; }
        });
        select.replaceChildren = (...options) => { select.options = options; value = ''; };
    }
    node('admissionStrings').textContent = JSON.stringify({ select: 'Select', optionsFailed: 'Options failed' });
    const body = { dataset: { authTenantId: '10', authSessionReference: 'session-1', authUserId: 'staff-1' } };
    const document = { body, documentElement: { lang: 'en-BD' }, getElementById: node,
        querySelectorAll: () => [], querySelector: () => ({ value: 'csrf-token' }),
        createElement: tag => ({ value: '', textContent: '', tagName: tag.toUpperCase(),
            append(...children) { this.children = [...(this.children || []), ...children]; },
            replaceChildren(...children) { this.children = children; },
            addEventListener() {}, focus() {} }), addEventListener() {} };
    let fetchHandler = null, fetchCount = 0, uuid = 0;
    const window = {};
    const context = { document, window, URLSearchParams, crypto: { randomUUID: () => 'id-' + (++uuid) },
        fetch: (...args) => { fetchCount++; return fetchHandler(...args); }, console };
    vm.runInNewContext(source, context, { filename: 'admissions/index.js' });
    return { node, body, window, api: context.__admissionTest,
        handle: fn => { fetchHandler = fn; }, fetchCount: () => fetchCount };
}

function options(values = {}) {
    const ids = (values.ids || [1, 2]);
    const items = () => ids.map(id => ({ id, name: `Choice ${id}` }));
    return { academicYears: items(), campuses: items(), academicLevels: items(),
        academicTerms: ids.flatMap(id => [{ id: id * 10 + 1, parentId: id, name: `Term ${id}` },
            { id: id * 10 + 2, parentId: id, name: `Term ${id} second` }]), openForms: [] };
}

const response = data => ({ ok: true, status: 200, async json() { return { success: true, data }; } });

test('refresh preserves academic and intake year/term/campus/unit without clearing dirty drafts', async () => {
    const ctx = setup();
    ctx.handle(async () => response(options()));
    await ctx.api.loadOptions();
    for (const id of ['academicYearId', 'campusId', 'academicUnitId', 'intakeYearId', 'intakeCampusId', 'intakeUnitId'])
        ctx.node(id).value = '2';
    ctx.node('academicTermId').options = [{ value: '' }, { value: '22' }];
    ctx.node('academicTermId').value = '22';
    ctx.node('intakeTermId').options = [{ value: '' }, { value: '22' }];
    ctx.node('intakeTermId').value = '22';
    for (const name of ['application', 'intake']) {
        ctx.api.guards[name] = { marks: 0, isDirty: () => true, markClean() { this.marks++; } };
    }
    await ctx.api.loadOptions();
    for (const id of ['academicYearId', 'campusId', 'academicUnitId', 'intakeYearId', 'intakeCampusId', 'intakeUnitId'])
        assert.equal(ctx.node(id).value, '2', id);
    assert.equal(ctx.node('academicTermId').value, '22');
    assert.equal(ctx.node('intakeTermId').value, '22');
    assert.equal(ctx.api.guards.application.marks, 0);
    assert.equal(ctx.api.guards.intake.marks, 0);
});

test('removed authorized option is cleared instead of replaced with a default', async () => {
    const ctx = setup();
    ctx.handle(async () => response(options()));
    await ctx.api.loadOptions();
    ctx.node('intakeYearId').value = '2';
    ctx.node('intakeCampusId').value = '2';
    ctx.node('intakeUnitId').value = '2';
    ctx.handle(async () => response(options({ ids: [1] })));
    await ctx.api.loadOptions();
    for (const id of ['intakeYearId', 'intakeCampusId', 'intakeUnitId'])
        assert.equal(ctx.node(id).value, '', `${id} must require a new explicit choice`);
    assert.equal(ctx.node('intakeTermId').value, '');
});

test('a published admission intake keeps its authoritative term after options refresh', async () => {
    const ctx = setup();
    const choices = options({ ids: [1] });
    choices.openForms = [{ reference: 'intake-a', title: 'Published', code: '2026',
        academicYearId: 1, academicTermId: 11, campusId: 1, academicLevelId: 1, fields: [] }];
    ctx.handle(async () => response(choices));
    await ctx.api.loadOptions();
    assert.equal(ctx.node('academicTermId').value, '11');
    ctx.node('academicTermId').value = '12';
    await ctx.api.loadOptions();
    assert.equal(ctx.node('academicTermId').value, '11', 'server-provided form term wins over stale local term');
});

test('newest options response wins; late failure never shows an obsolete error', async () => {
    const ctx = setup(), requests = [];
    ctx.handle(() => new Promise(resolve => requests.push(resolve)));
    const old = ctx.api.loadOptions();
    const current = ctx.api.loadOptions();
    requests[1](response(options({ ids: [3] })));
    await current;
    requests[0]({ ok: false, status: 500, json: async () => null });
    await old;
    assert.equal(ctx.api.state.options.academicYears[0].id, 3);
    assert.equal(ctx.node('admissionAlert').textContent, '');
});

test('tenant or user/session switch blocks pending option response and any next write', async () => {
    const ctx = setup(), requests = [];
    ctx.handle(() => new Promise(resolve => requests.push(resolve)));
    const pending = ctx.api.loadOptions();
    ctx.body.dataset.authTenantId = '11';
    requests[0](response(options({ ids: [99] })));
    await pending;
    assert.equal(ctx.api.state.options, null);
    await ctx.api.loadOptions();
    assert.equal(ctx.fetchCount(), 1);
    assert.throws(() => ctx.api.apiOptions('POST', {}), /Session changed/);
});

test('staff admission retries identical command once, rotates key on edit and only clears after success', async () => {
    const ctx = setup(), posts = [];
    const choices = options({ ids: [1] });
    choices.openForms = [{ reference: 'intake-a', title: 'Current intake', code: '2026',
        academicYearId: 1, academicTermId: 11, campusId: 1, academicLevelId: 1, fields: [] }];
    ctx.handle(async () => response(choices));
    await ctx.api.loadOptions();
    ctx.node('applicantName').value = 'Applicant A';
    ctx.node('dateOfBirth').value = '1995-01-01';
    const form = ctx.node('admissionForm');
    const submit = () => ctx.api.submitApplication({ preventDefault() {}, currentTarget: form });
    ctx.handle((url, request) => {
        if (request.method === 'POST') return new Promise(resolve => posts.push({ resolve, body: JSON.parse(request.body) }));
        return response({ items: [], totalPages: 1, totalCount: 0 });
    });
    const first = submit();
    await submit();
    assert.equal(posts.length, 1, 'only one in-flight create is allowed');
    posts[0].resolve({ ok: false, status: 503, json: async () => ({ success: false }) });
    await first;
    assert.equal(form.resets || 0, 0, 'failed write must not discard entered form');
    assert.equal(ctx.node('admissionSubmitFeedback').className, 'admission-submit-feedback',
        'server failure is presented beside the application form');
    assert.match(ctx.node('admissionSubmitFeedback').children[1].textContent, /Check the application list before retrying/);
    const retry = submit();
    assert.equal(posts[1].body.clientRequestId, posts[0].body.clientRequestId);
    posts[1].resolve({ ok: false, status: 503, json: async () => ({ success: false }) });
    await retry;
    ctx.node('applicantName').value = 'Applicant A Corrected';
    const changed = submit();
    assert.notEqual(posts[2].body.clientRequestId, posts[1].body.clientRequestId);
    posts[2].resolve({ ok: true, status: 200, json: async () => ({ success: true, data: { applicationNumber: 'A-26-001' } }) });
    await changed;
    assert.equal(form.resets, 1);
    assert.equal(ctx.node('admissionSubmissionReceipt').hidden, false);
    assert.equal(ctx.node('admissionSubmitFeedback').className, 'd-none',
        'successful receipt is shown without a duplicate global success alert');
    assert.equal(ctx.node('admissionAlert').textContent, '');
});

test('a completed old-tenant admission response cannot display applicant receipt or clear draft', async () => {
    const ctx = setup(), posts = [];
    const choices = options({ ids: [1] });
    choices.openForms = [{ reference: 'intake-a', title: 'Current intake', code: '2026',
        academicYearId: 1, academicTermId: 11, campusId: 1, academicLevelId: 1, fields: [] }];
    ctx.handle(async () => response(choices));
    await ctx.api.loadOptions();
    ctx.node('applicantName').value = 'Student A';
    ctx.node('dateOfBirth').value = '1995-01-01';
    const form = ctx.node('admissionForm');
    ctx.handle(() => new Promise(resolve => posts.push(resolve)));
    const writing = ctx.api.submitApplication({ preventDefault() {}, currentTarget: form });
    ctx.body.dataset.authSessionReference = 'session-2';
    posts[0]({ ok: true, status: 200, json: async () => ({ success: true, data: { applicationNumber: 'A-26-002' } }) });
    await writing;
    assert.equal(form.resets || 0, 0);
    assert.equal(ctx.node('admissionSubmissionReceipt').children, undefined);
    assert.equal(ctx.node('admissionAlert').textContent, '');
});

test('cached admission page export is unavailable after tenant/session switch', () => {
    const ctx = setup(), downloads = [];
    ctx.window.EduOSPageCsv = { download: (...args) => downloads.push(args) };
    ctx.api.state.listPageRows = [{ applicationNumber: 'A-1', applicantName: 'Private',
        academicLevelName: 'Class 1', campusName: 'Main', maskedMobile: '01***', state: 2 }];
    ctx.api.exportApplicationPage();
    assert.equal(downloads.length, 1);
    ctx.body.dataset.authUserId = 'other-user';
    ctx.api.exportApplicationPage();
    assert.equal(downloads.length, 1, 'previous user page must not be exported');
});
