(() => {
'use strict';
const root = document.getElementById('feeHeadAccounting');
if (!root) return;
const writable = root.dataset.write === 'true', base = '/api/finance/fee-head-accounting';
const el = id => document.getElementById(id);
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
let heads = [], accounts = [];
let busy = false, readSequence = 0;
const context = [document.body?.dataset.authTenantId || '', document.body?.dataset.authSessionReference || ''];
const live = () => context[0] === (document.body?.dataset.authTenantId || '') &&
    context[1] === (document.body?.dataset.authSessionReference || '') &&
    window.EduOSSessionTransitionPending?.() !== true;
const guard = writable ? window.EduOSFormGuard?.create(el('feeHeadForm')) : null;
function lockForm() {
    const controls = [...el('feeHeadForm').querySelectorAll('input, select, button')];
    const prior = controls.map(x => x.disabled);
    controls.forEach(x => { x.disabled = true; });
    return () => controls.forEach((x, index) => { x.disabled = prior[index]; });
}
const frequency = {1:'One-time',2:'Monthly',3:'Quarterly',4:'Half-yearly',5:'Yearly',6:'Per term'};
function notice(message, error = false) {
    const box = el('feeHeadNotice'); box.textContent = message;
    box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
}
async function request(path = '', method = 'GET', body) {
    if (!live()) throw new Error('Institution session changed. Reload the page.');
    const headers = {Accept:'application/json'};
    if (method !== 'GET') {
        if (!token) throw new Error('Antiforgery token missing. Reload.');
        headers.RequestVerificationToken = token;
    }
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(base + path, {method, credentials:'same-origin', cache:'no-store',
        headers, ...(body === undefined ? {} : {body:JSON.stringify(body)})});
    const data = await response.json().catch(() => null);
    if (!live()) throw new Error('Institution session changed. Reload the page.');
    if (!response.ok || !data?.success) throw new Error(data?.message || 'Request failed.');
    return data.data;
}
function option(value, label) {
    const node = document.createElement('option'); node.value = String(value); node.textContent = label; return node;
}
function cell(value) {
    const node = document.createElement('td'); node.textContent = value == null ? '' : String(value); return node;
}
function ledgerSelect(type, selected) {
    const select = document.createElement('select'); select.className = 'form-select form-select-sm';
    select.append(option('', 'Select ' + (type === 4 ? 'income' : 'asset') + ' ledger'));
    for (const a of accounts.filter(x => x.accountType === type))
        select.append(option(a.id, a.code + ' — ' + a.name));
    select.value = selected == null ? '' : String(selected);
    return select;
}
function render() {
    const body = el('feeHeadRows'); body.replaceChildren();
    for (const h of heads) {
        const row = document.createElement('tr');
        for (const value of [h.code, h.name, frequency[h.defaultFrequency] || h.defaultFrequency]) row.append(cell(value));
        const income = ledgerSelect(4, h.incomeAccountId), receivable = ledgerSelect(1, h.receivableAccountId);
        income.disabled = !writable; receivable.disabled = !writable;
        const incomeCell = document.createElement('td'), arCell = document.createElement('td');
        incomeCell.append(income); arCell.append(receivable); row.append(incomeCell, arCell);
        row.append(cell(h.readyForPosting ? 'Ready' : 'Not configured'), cell(h.hasInvoiceHistory ? 'Yes' : 'No'));
        if (writable) {
            const action = document.createElement('td');
            const save = document.createElement('button'); save.type = 'button';
            save.className = 'btn btn-sm btn-outline-primary me-1'; save.textContent = 'Save mapping';
            save.addEventListener('click', () => run(async () => {
                if (busy || !live()) return;
                const incomeAccountId = Number(income.value), receivableAccountId = Number(receivable.value);
                if (!incomeAccountId || !receivableAccountId) throw new Error('Select both posting accounts.');
                busy = true; save.disabled = true; income.disabled = true; receivable.disabled = true;
                try {
                    await request('/heads/' + h.id + '/accounts', 'PUT',
                        {incomeAccountId, receivableAccountId, rowVersion:h.rowVersion});
                    notice('Mapping saved. Automatic GL posting is not enabled.');
                    try { await load(); } catch (error) { notice('Mapping saved; refresh the register. ' + error.message); }
                } finally { busy = false; save.disabled = false; income.disabled = false; receivable.disabled = false; }
            }));
            const edit = document.createElement('button'); edit.type = 'button';
            edit.className = 'btn btn-sm btn-outline-secondary'; edit.textContent = 'Edit';
            edit.addEventListener('click', () => {
                if (busy || !live() || guard && !guard.confirmDiscard()) return;
                el('feeHeadId').value = h.id; el('feeHeadVersion').value = h.rowVersion;
                el('feeHeadCode').value = h.code; el('feeHeadName').value = h.name;
                el('feeHeadFrequency').value = h.defaultFrequency;
                el('feeHeadRefundable').checked = h.isRefundable; el('feeHeadActive').checked = h.isActive;
                el('feeHeadCode').focus();
                guard?.markClean();
            });
            action.append(save, edit); row.append(action);
        }
        body.append(row);
    }
    if (!heads.length) {
        const row = document.createElement('tr'), empty = cell('No institutional fee heads configured.');
        empty.colSpan = writable ? 8 : 7; row.append(empty); body.append(row);
    }
}
async function load() {
    const sequence = ++readSequence;
    const data = await request();
    if (sequence !== readSequence || !live()) return;
    heads = data.feeHeads || []; accounts = data.postingAccounts || []; render();
}
function reset() {
    el('feeHeadForm').reset(); el('feeHeadId').value = ''; el('feeHeadVersion').value = '';
    guard?.markClean();
}
async function run(action) { try { await action(); } catch (error) { notice(error.message, true); } }
if (writable) {
    el('feeHeadReset').addEventListener('click', () => {
        if (!busy && guard?.confirmDiscard() !== false) reset();
    });
    el('feeHeadForm').addEventListener('submit', event => {
        event.preventDefault();
        run(async () => {
            if (busy || !live()) return;
            const id = el('feeHeadId').value;
            const body = {code:el('feeHeadCode').value.trim(), name:el('feeHeadName').value.trim(),
                defaultFrequency:Number(el('feeHeadFrequency').value), isRefundable:el('feeHeadRefundable').checked,
                isActive:el('feeHeadActive').checked, rowVersion:el('feeHeadVersion').value || null};
            busy = true; const unlock = lockForm();
            try {
                await request('/heads' + (id ? '/' + id : ''), id ? 'PUT' : 'POST', body);
                reset(); notice('Fee head saved.');
                try { await load(); } catch (error) { notice('Fee head saved; refresh the register. ' + error.message); }
            } finally { busy = false; unlock(); }
        });
    });
}
const observer = new MutationObserver(() => {
    if (live()) return;
    ++readSequence; heads = []; accounts = []; el('feeHeadRows').replaceChildren();
    if (writable) reset();
    notice('Institution session changed. Reload the page.', true); observer.disconnect();
});
observer.observe(document.body, { attributes: true,
    attributeFilter: ['data-auth-tenant-id', 'data-auth-session-reference'] });
run(load);
})();
