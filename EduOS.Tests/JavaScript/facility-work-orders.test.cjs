const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const script = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/wwwroot/js/facilities/work-orders.js'), 'utf8');
const view = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/Views/FacilityWorkOrders/Index.cshtml'), 'utf8');

test('facilities workflow is bilingual, mobile and escapes untrusted data', () => {
    assert.match(script, /textContent = value/);
    assert.doesNotMatch(script, /innerHTML/);
    assert.match(view, /table-responsive/);
    assert.match(view, /data-bn=/);
    assert.match(view, /aria-live="polite"/);
    assert.match(view, /col-12 col-xl-7/);
});

test('workflow posts antiforgery and retries the same idempotency command', () => {
    assert.match(script, /RequestVerificationToken = token/);
    assert.match(script, /credentials: 'same-origin'/);
    assert.match(script, /body\.clientCommandId = commandId\(key, body\)/);
    assert.match(script, /body\.clientRequestId = commandId\(key, body\)/);
    assert.match(script, /rowVersion:/);
    assert.match(script, /\/history\?page=/);
    assert.match(script, /facilityExport/);
    assert.match(script, /\/dashboard\?/);
    assert.match(script, /wfDashboard/);
    assert.match(view, /Service dashboard/);
});

test('independent cancellation is bilingual, reasoned and terminal, without stock posting', () => {
    for (const action of ['cancelrequest', 'cancelapprove', 'cancelreject'])
        assert.match(script, new RegExp(action));
    assert.match(script, /Request cancellation/);
    assert.match(script, /বাতিলের আবেদন/);
    assert.match(script, /CancellationPending/);
    assert.match(script, /cancellationPending/);
    assert.match(script, /cancelled/);
});

test('the UI never claims to post Inventory or Finance', () => {
    assert.match(view, /Parts and Finance posting are not enabled here/);
    assert.doesNotMatch(script, /\/api\/inventory\/movements\/post/);
});
test('scheduled work can be released by manager with a reason and rowversion', () => {
    assert.match(script, /options\.push\('schedule', 'unschedule'\)/);
    assert.match(script, /Release schedule/);
    assert.match(script, /সময়সূচি বাতিল/);
    assert.match(script, /el\('wfNote'\)\.required = \['reject', 'pause', 'complete', 'verify', 'return', 'unschedule', 'escalate', 'cancelrequest', 'cancelapprove', 'cancelreject'\]\.includes\(action\)/);
    assert.match(script, /action === 'unschedule' \? 5 : 0/);
    assert.match(script, /selected\.workOrderRowVersion/);
});

test('overdue SLA queue and manual escalation are scoped and bilingual', () => {
    assert.match(view, /id="wfOverdueOnly"/);
    assert.match(script, /q\.set\('overdueOnly', 'true'\)/);
    assert.match(script, /Escalate overdue SLA/);
    assert.match(script, /বিলম্বিত কাজ এস্কেলেট করুন/);
    assert.match(script, /row\.requesterUserId !== currentUserId/);
    assert.match(script, /row\.technicianUserId === currentUserId/);
    assert.match(script, /selected\.workOrderRowVersion/);
});

test('independent verifier can return unsafe completion for correction, with bilingual action and reason', () => {
    assert.match(script, /options\.push\('verify', 'return'\)/);
    assert.match(script, /Return for correction/);
    assert.match(script, /সংশোধনের জন্য ফেরত/);
    assert.match(script, /\['return', 'escalate', 'cancelrequest', 'cancelapprove', 'cancelreject'\]\.includes\(action\) \? 10/);
    assert.match(script, /'escalate', 'return', 'cancelrequest', 'cancelapprove', 'cancelreject'\]\.includes\(action\) \? 500/);
    assert.match(script, /actionLabels\[event\.actionCode\]/);
    assert.match(script, /selected\.workOrderRowVersion/);
});

test('an uncertain submission retains its command ID only for an identical payload', () => {
    const vm = require('node:vm');
    const begin = script.indexOf('    const pending = new Map();');
    const end = script.indexOf('    function notice(', begin);
    assert.ok(begin >= 0 && end > begin, 'extract actual browser command helper');
    let sequence = 0;
    const { commandId, pending } = vm.runInNewContext(
        script.slice(begin, end) + '\n({ commandId, pending })',
        { crypto: { randomUUID: () => 'command-' + ++sequence }, t: en => en });
    const request = { campusId: 12, facilitySpaceId: 7, summary: 'Broken light' };
    const first = commandId('create:12:7', request);
    assert.equal(commandId('create:12:7', { ...request }), first);
    assert.throws(() => commandId('create:12:7',
        { ...request, summary: 'Broken fan' }), /Reload and check the request/);
    assert.equal(commandId('create:12:8',
        { ...request, facilitySpaceId: 8 }), 'command-2');
    pending.delete('create:12:7');
    assert.equal(commandId('create:12:7',
        { ...request, summary: 'Broken fan' }), 'command-3');
    const transition = { rowVersion: 'AAAB', note: 'Original approval note' };
    const transitionId = commandId('51:verify', transition);
    assert.equal(commandId('51:verify', { ...transition }), transitionId);
    assert.throws(() => commandId('51:verify',
        { ...transition, note: 'Changed approval note' }), /Reload and check the request/);
});

test('confirmed validation rejection exposes status so the form can permit corrected details', async () => {
    const vm = require('node:vm');
    const begin = script.indexOf('    async function api(');
    const end = script.indexOf('    const cell =', begin);
    assert.ok(begin >= 0 && end > begin);
    const context = {
        base: '/api/facilities/work-orders', token: 'test-csrf',
        t: en => en, sessionCurrent: () => true, verifySession: async () => {},
        lockPage: () => {}, fetch: async () => ({
            ok: false, status: 400,
            json: async () => ({ success: false, message: 'Invalid summary' })
        })
    };
    const api = vm.runInNewContext(script.slice(begin, end) + '\napi', context);
    await assert.rejects(api('', 'POST', { summary: 'x' }),
        error => error.status === 400 && error.message === 'Invalid summary');
    assert.equal((script.match(/error\.status === 400 \|\| error\.status === 422/g) || []).length, 2);
    assert.match(script, /pending\.delete\(key\)/);
});

test('Facilities list ignores older campus results after a new query', async () => {
    const vm = require('node:vm');
    const a = script.indexOf('    async function dashboard('), b = script.indexOf('    async function history()', a);
    assert.ok(a >= 0 && b > a);
    const node = () => ({ children: [], replaceChildren(...v) { this.children = v; },
        append(...v) { this.children.push(...v); }, addEventListener() {} });
    const elements = new Map(), el = id => {
        if (!elements.has(id)) elements.set(id, node()); return elements.get(id);
    };
    el('wfCampusFilter').value = '1'; el('wfStateFilter').value = ''; el('wfOverdueOnly').checked = false;
    let resolveOld, calls = 0;
    const old = new Promise(resolve => { resolveOld = resolve; });
    const context = { blocked: false, listGeneration: 0, page: 1, pages: 1, selected: null, el,
        URLSearchParams, document: { createElement: node }, t: en => en,
        cell: value => ({ textContent: String(value) }), select() {}, notice() {},
        api: async url => url.startsWith('/dashboard') ? {} : calls++ === 0 ? old :
            { items: [{ id: 2, facilitySpaceId: 8, summary: 'New campus',
                priorityCode: 'Normal', stateCode: 'Submitted' }], totalPages: 1 } };
    const { load } = vm.runInNewContext(script.slice(a, b) + '\n({ load })', context);
    const earlier = load();
    el('wfCampusFilter').value = '2'; await load();
    assert.equal(el('wfRows').children[0].children[0].textContent, '2');
    resolveOld({ items: [{ id: 1, facilitySpaceId: 7, summary: 'Old campus',
        priorityCode: 'High', stateCode: 'Submitted' }], totalPages: 1 });
    await earlier;
    assert.equal(el('wfRows').children[0].children[0].textContent, '2');
});

test('Facilities history ignores late responses after selecting another work order', async () => {
    const vm = require('node:vm');
    const a = script.indexOf('    async function history()'), b = script.indexOf("    el('workflowSearch').addEventListener", a);
    assert.ok(a >= 0 && b > a);
    const node = () => ({ children: [], replaceChildren(...v) { this.children = v; },
        append(...v) { this.children.push(...v); }, addEventListener() {},
        classList: { add() {} } });
    const box = node();
    let resolveOld, notices = 0;
    const old = new Promise(resolve => { resolveOld = resolve; });
    const context = { blocked: false, selected: { id: 11 }, historyGeneration: 0,
        historyPage: 1, historyPages: 1, el: () => box,
        document: { createElement: node }, t: en => en, actionLabels: {}, bn: false,
        notice() { notices++; },
        api: async url => url.includes('/11/') ? old : {
            totalPages: 1, items: [{ actionCode: 'complete', fromState: 'InProgress',
                toState: 'PendingVerification', actorUserId: 42,
                occurredAtUtc: '2026-11-01T00:00:00Z', note: 'New evidence' }] } };
    const { history } = vm.runInNewContext(script.slice(a, b) + '\n({ history })', context);
    const earlier = history(); context.selected = { id: 12 }; await history();
    assert.match(box.children[0].children[0].textContent, /New evidence/);
    resolveOld({ totalPages: 1, items: [{ actionCode: 'reject', note: 'Old evidence' }] });
    await earlier;
    assert.match(box.children[0].children[0].textContent, /New evidence/);
    assert.equal(notices, 0);
});

test('Facilities search, pagination and late POST cannot restore stale actions', () => {
    assert.match(script, /function clearSelection\(\)/);
    assert.match(script, /clearSelection\(\); load\(\)/);
    assert.match(script, /generation !== listGeneration/);
    assert.match(script, /generation !== historyGeneration \|\| selected\?\.id !== requestId/);
    assert.match(script, /if \(!blocked && selected\?\.id === result\.id\) select\(result\)/);
});

test('work orders fail closed when tenant changes during an in-flight read', async () => {
    const vm = require('node:vm');
    const a = script.indexOf('    function lockPage()');
    const b = script.indexOf('    const cell =', a);
    assert.ok(a > 0 && b > a, 'execute the real session and API implementation');
    const nodes = new Map();
    const el = id => {
        if (!nodes.has(id)) nodes.set(id, {
            textContent: '', disabled: false, className: '',
            replaceChildren() { this.children = []; },
            reset() { this.resetCalled = true; },
            querySelectorAll() { return [{ disabled: false }]; },
            classList: { add() {} }
        });
        return nodes.get(id);
    };
    const body = { dataset: { authSessionReference: 'A'.repeat(64), authTenantId: '12' } };
    const shield = { hidden: true };
    let network = 0, switchOnVerify = false;
    const context = {
        el, document: { body }, window: {
            EduOSSessionTransitionPending: () => false,
            EduOSVerifySessionContext: async () => {
                if (switchOnVerify) { body.dataset.authTenantId = '99'; shield.hidden = false; }
            }
        }, shield, session: { reference: body.dataset.authSessionReference, tenant: '12' },
        blocked: false, listGeneration: 0, historyGeneration: 0, selected: { id: 7 },
        pending: new Map([['7:verify', { id: 'pending' }]]),
        t: en => en, token: 'csrf', base: '/api/facilities/work-orders',
        fetch: async () => {
            network++;
            return { ok: true, status: 200, json: async () =>
                ({ success: true, data: { items: [{ id: 7, summary: 'private' }] } }) };
        }
    };
    const { api, sessionCurrent } = vm.runInNewContext(script.slice(a, b) +
        '\n({ api, sessionCurrent })', context);
    assert.equal(sessionCurrent(), true);
    assert.equal((await api('', 'GET')).items[0].summary, 'private');
    switchOnVerify = true;
    await assert.rejects(api('', 'GET'), /session-changed/);
    assert.equal(context.blocked, true);
    assert.equal(context.selected, null);
    assert.equal(context.pending.size, 0);
    assert.equal(el('wfRows').children.length, 0);
    assert.equal(el('facilityExport').disabled, true);
    assert.match(el('workflowAlert').textContent, /Institution session changed/);
    const before = network;
    await assert.rejects(api('', 'POST', { note: 'must not send' }), /session-changed/);
    assert.equal(network, before, 'no POST after tenant switch');
});

test('work-order exports and duplicate commands use the existing session shield', () => {
    assert.match(script, /await window\.EduOSVerifySessionContext\(\)/);
    assert.match(script, /shield\.hidden !== true/);
    assert.match(script, /if \(method !== 'GET'\) await verifySession\(\)/);
    assert.match(script, /await verifySession\(\);\s*const data = await response\.json/);
    assert.match(script, /facilityExport'\)\.addEventListener\('click', async/);
    assert.match(script, /if \(blocked \|\| creating\) return/);
    assert.match(script, /if \(!selected \|\| blocked \|\| transitioning\) return/);
});
