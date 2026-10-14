(() => {
'use strict';
const root = document.getElementById('feeHeadAccounting');
if (!root) return;
const writable = root.dataset.write === 'true', base = '/api/finance/fee-head-accounting';
const el = id => document.getElementById(id);
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
let heads = [], accounts = [];
const frequency = {1:'One-time',2:'Monthly',3:'Quarterly',4:'Half-yearly',5:'Yearly',6:'Per term'};
function notice(message, error = false) {
    const box = el('feeHeadNotice'); box.textContent = message;
    box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
}
async function request(path = '', method = 'GET', body) {
    const headers = {Accept:'application/json'};
    if (method !== 'GET') {
        if (!token) throw new Error('Antiforgery token missing. Reload.');
        headers.RequestVerificationToken = token;
    }
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(base + path, {method, credentials:'same-origin', cache:'no-store',
        headers, ...(body === undefined ? {} : {body:JSON.stringify(body)})});
    const data = await response.json().catch(() => null);
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
                const incomeAccountId = Number(income.value), receivableAccountId = Number(receivable.value);
                if (!incomeAccountId || !receivableAccountId) throw new Error('Select both posting accounts.');
                save.disabled = true;
                try {
                    await request('/heads/' + h.id + '/accounts', 'PUT',
                        {incomeAccountId, receivableAccountId, rowVersion:h.rowVersion});
                    await load(); notice('Mapping saved. Automatic GL posting is not enabled.');
                } finally { save.disabled = false; }
            }));
            const edit = document.createElement('button'); edit.type = 'button';
            edit.className = 'btn btn-sm btn-outline-secondary'; edit.textContent = 'Edit';
            edit.addEventListener('click', () => {
                el('feeHeadId').value = h.id; el('feeHeadVersion').value = h.rowVersion;
                el('feeHeadCode').value = h.code; el('feeHeadName').value = h.name;
                el('feeHeadFrequency').value = h.defaultFrequency;
                el('feeHeadRefundable').checked = h.isRefundable; el('feeHeadActive').checked = h.isActive;
                el('feeHeadCode').focus();
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
    const data = await request();
    heads = data.feeHeads || []; accounts = data.postingAccounts || []; render();
}
function reset() {
    el('feeHeadForm').reset(); el('feeHeadId').value = ''; el('feeHeadVersion').value = '';
}
async function run(action) { try { await action(); } catch (error) { notice(error.message, true); } }
if (writable) {
    el('feeHeadReset').addEventListener('click', reset);
    el('feeHeadForm').addEventListener('submit', event => {
        event.preventDefault();
        run(async () => {
            const id = el('feeHeadId').value;
            const body = {code:el('feeHeadCode').value.trim(), name:el('feeHeadName').value.trim(),
                defaultFrequency:Number(el('feeHeadFrequency').value), isRefundable:el('feeHeadRefundable').checked,
                isActive:el('feeHeadActive').checked, rowVersion:el('feeHeadVersion').value || null};
            await request('/heads' + (id ? '/' + id : ''), id ? 'PUT' : 'POST', body);
            reset(); await load(); notice('Fee head saved.');
        });
    });
}
run(load);
})();
