(() => {
'use strict';
const root = document.getElementById('feePlanApp');
if (!root) return;
const el = id => document.getElementById(id);
const base = '/api/finance/fees';
const token = document.querySelector('meta[name="request-verification-token"]')?.content ||
    root.querySelector('input[name="__RequestVerificationToken"]')?.value;
const status = { 1: 'Draft', 2: 'Submitted', 3: 'Approved', 4: 'Cancelled' };
function money(value) {
    const raw = String(value ?? '0');
    if (!/^-?\d+(?:\.\d{1,2})?$/.test(raw)) return 'Invalid amount';
    const parts = raw.split('.');
    return parts[0] + '.' + (parts[1] || '').padEnd(2, '0');
}
let options, page = 1, total = 0, rows = [], selected, searchController, searchTimer;
let draftKey, transitionKey, invoiceKey;
const cell = value => { const td = document.createElement('td'); td.textContent = String(value ?? ''); return td; };
const option = (value, label) => { const o = document.createElement('option'); o.value = String(value); o.textContent = label; return o; };
function notice(message, error = false) {
    const n = el('feePlanNotice'); n.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
    n.textContent = message; n.focus();
}
async function api(path, method = 'GET', payload) {
    const headers = { Accept: 'application/json' };
    if (method !== 'GET') {
        if (!token) throw Error('Security token missing. Reload the page.');
        headers.RequestVerificationToken = token;
        headers['Content-Type'] = 'application/json';
    }
    const response = await fetch(base + path, {
        method, credentials: 'same-origin', cache: 'no-store', headers,
        ...(payload === undefined ? {} : { body: JSON.stringify(payload) })
    });
    const data = await response.json().catch(() => null);
    if (!response.ok || !data?.success) throw Error(data?.message || 'Finance request failed.');
    return data.data;
}
function refreshStructures() {
    const batch = options?.academicBatches?.find(x => String(x.id) === el('feePlanBatch').value);
    const select = el('feePlanStructure');
    select.replaceChildren(option('', 'Select structure'));
    if (!batch) return;
    for (const x of options.feeStructures || []) {
        if (x.campusId !== batch.campusId || x.academicYearId !== batch.academicYearId ||
            (x.academicBatchId != null && x.academicBatchId !== batch.id) ||
            (x.academicLevelId != null && x.academicLevelId !== batch.academicLevelId) ||
            (x.academicProgramId != null && x.academicProgramId !== batch.academicProgramId)) continue;
        select.append(option(x.id, x.name));
    }
}
async function searchStudents() {
    const term = el('feePlanStudentSearch').value.trim();
    if (searchController) searchController.abort();
    const select = el('feePlanStudent');
    select.replaceChildren(option('', term.length < 2 ? 'Enter at least two characters' : 'Searching…'));
    if (term.length < 2) return;
    searchController = new AbortController();
    try {
        const response = await fetch(base + '/students/search?search=' + encodeURIComponent(term) + '&take=30',
            { credentials: 'same-origin', cache: 'no-store', signal: searchController.signal,
                headers: { Accept: 'application/json' } });
        const result = await response.json();
        if (!response.ok || !result.success) throw Error(result.message || 'Student search failed.');
        select.replaceChildren(option('', 'Select matched student'));
        for (const x of result.data || []) select.append(option(x.reference, x.studentCode + ' — ' + x.name));
    } catch (error) {
        if (error.name !== 'AbortError') notice(error.message, true);
    }
}
async function register() {
    const campusId = el('feePlanCampus').value;
    const query = new URLSearchParams({ page: String(page), pageSize: '25' });
    if (campusId) query.set('campusId', campusId);
    el('feePlanRefresh').disabled = true;
    try {
        const data = await api('/plans?' + query);
        rows = data.items || []; total = data.totalCount || 0;
        const body = document.createDocumentFragment();
        for (const item of rows) {
            const tr = document.createElement('tr');
            for (const value of [item.studentCode + ' — ' + item.studentName,
                item.startMonth + ' to ' + item.endMonth, item.campusId,
                status[item.stateCode] || 'Unknown', money(item.grossAmount)])
                tr.append(cell(value));
            tr.children[4].className = 'text-end';
            const action = document.createElement('td'); action.className = 'd-print-none';
            const button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-outline-primary btn-sm';
            button.textContent = 'View'; button.addEventListener('click', () => detail(item.reference));
            action.append(button); tr.append(action); body.append(tr);
        }
        if (!rows.length) {
            const tr = document.createElement('tr'), td = cell('No agreements in this scope.');
            td.colSpan = 6; td.className = 'text-center text-muted p-3'; tr.append(td); body.append(tr);
        }
        el('feePlanRows').replaceChildren(body);
        el('feePlanPage').textContent = 'Page ' + page + ' · ' + total + ' plans';
        el('feePlanPrevious').disabled = page <= 1;
        el('feePlanNext').disabled = page * 25 >= total;
        el('feePlanCsv').disabled = !rows.length;
        el('feePlanPrint').disabled = !rows.length;
    } catch (error) { notice(error.message, true); }
    finally { el('feePlanRefresh').disabled = false; }
}
async function detail(reference) {
    billingAudit = null;
    el('feePlanAudit').classList.add('d-none');
    try {
        selected = await api('/plans/' + encodeURIComponent(reference));
        const body = document.createDocumentFragment();
        for (const line of selected.lines || []) {
            const tr = document.createElement('tr');
            for (const value of [line.monthStart, line.dueDate,
                line.feeHeadCode + ' — ' + line.feeHeadName, money(line.grossAmount)])
                tr.append(cell(value));
            tr.lastChild.className = 'text-end';
            const invoice = document.createElement('td');
            if (line.invoiceReference) {
                const link = document.createElement('a');
                link.href = '/FeeOperations/Index';
                link.textContent = line.invoiceNumber || 'Issued';
                link.title = 'Invoice recorded. Open Fees & Billing for authorized invoice access.';
                invoice.append(link);
            } else invoice.textContent = '—';
            tr.append(invoice); body.append(tr);
        }
        el('feePlanDetailRows').replaceChildren(body);
        el('feePlanDetailTitle').textContent = selected.studentCode + ' · ' +
            (status[selected.stateCode] || 'Plan') + ' · Gross ' + money(selected.grossAmount) +
            ' ' + selected.currencyCode;
        el('feePlanHistory').textContent = (selected.events || [])
            .map(x => (status[x.actionCode] || x.actionCode) + ' by user #' + x.actorUserId +
                ' at ' + x.eventAt + (x.reason ? ' · ' + x.reason : '')).join(' | ') || 'Draft';
        el('feePlanSubmit').hidden = selected.stateCode !== 1;
        const monthSelect = el('feePlanIssueMonth');
        const months = [...new Set((selected.lines || []).map(x => x.monthStart))];
        const billed = new Set((selected.lines || []).filter(x => x.invoiceReference).map(x => x.monthStart));
        monthSelect.replaceChildren(option('', 'Choose unbilled month'));
        for (const month of months.filter(x => !billed.has(x))) monthSelect.append(option(month, month));
        monthSelect.hidden = selected.stateCode !== 3 || monthSelect.options.length <= 1;
        el('feePlanIssue').hidden = monthSelect.hidden;
        invoiceKey = null;
        if (el('feePlanApprove')) el('feePlanApprove').hidden = selected.stateCode !== 2;
        if (el('feePlanCancel')) el('feePlanCancel').hidden = selected.stateCode === 4;
        el('feePlanDetail').classList.remove('d-none');
        transitionKey = null;
    } catch (error) { notice(error.message, true); }
}
async function transition(action, reason = '') {
    if (!selected) return;
    if (action === 'cancel' && !reason.trim()) return;
    if (!transitionKey || transitionKey.action !== action ||
        transitionKey.reference !== selected.reference || transitionKey.reason !== reason)
        transitionKey = { action, reference: selected.reference, reason, id: crypto.randomUUID() };
    try {
        const reference = selected.reference;
        await api('/plans/' + encodeURIComponent(reference) + '/' + action, 'POST', {
            clientRequestId: transitionKey.id, rowVersion: selected.rowVersion, reason: reason || null
        });
        transitionKey = null;
        await detail(reference); await register();
        notice('Agreement transition recorded.');
    } catch (error) { notice(error.message, true); }
}
function csvCell(value) {
    let text = String(value ?? '');
    if (/^[\s\u0000-\u001f]*[=+\-@]/.test(text)) text = "'" + text;
    return '"' + text.replace(/"/g, '""') + '"';
}
function exportPage() {
    if (!rows.length) return;
    const lines = [['Student','Name','Start month','End month','Campus','State','Gross BDT']];
    for (const x of rows) lines.push([x.studentCode,x.studentName,x.startMonth,x.endMonth,
        x.campusId,status[x.stateCode] || '',money(x.grossAmount)]);
    const csv = '\uFEFF' + lines.map(x => x.map(csvCell).join(',')).join('\r\n') + '\r\n';
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const link = document.createElement('a'); link.href = url; link.download = 'fee-agreements-page.csv';
    document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
el('feePlanStudentSearch').addEventListener('input', () => {
    draftKey = null; clearTimeout(searchTimer); searchTimer = setTimeout(searchStudents, 350);
});
for (const id of ['feePlanStudent','feePlanBatch','feePlanStructure','feePlanMonth','feePlanMonths'])
    el(id).addEventListener('change', () => { draftKey = null; });
el('feePlanBatch').addEventListener('change', refreshStructures);
el('feePlanCreate').addEventListener('submit', async event => {
    event.preventDefault();
    if (!draftKey) draftKey = crypto.randomUUID();
    const payload = {
        clientRequestId: draftKey, studentReference: el('feePlanStudent').value,
        academicBatchId: Number(el('feePlanBatch').value),
        feeStructureId: Number(el('feePlanStructure').value),
        startMonth: el('feePlanMonth').value + '-01', months: Number(el('feePlanMonths').value)
    };
    el('feePlanSave').disabled = true;
    try {
        const plan = await api('/plans', 'POST', payload);
        draftKey = null; await register(); await detail(plan.reference);
        notice('Gross agreement draft saved; no invoice or payment was created.');
    } catch (error) { notice(error.message, true); }
    finally { el('feePlanSave').disabled = false; }
});
el('feePlanCampus').addEventListener('change', () => { page = 1; register(); });
el('feePlanRefresh').addEventListener('click', register);
el('feePlanPrevious').addEventListener('click', () => { if (page > 1) { page--; register(); } });
el('feePlanNext').addEventListener('click', () => { if (page * 25 < total) { page++; register(); } });
el('feePlanCsv').addEventListener('click', exportPage);
el('feePlanPrint').addEventListener('click', () => { if (rows.length) window.print(); });
el('feePlanSubmit').addEventListener('click', () => transition('submit'));

let billingAudit = null;
async function openBillingAudit() {
    if (!selected) return;
    const reference = selected.reference;
    const button = el('feePlanAuditOpen');
    button.disabled = true;
    billingAudit = null;
    el('feePlanAudit').classList.add('d-none');
    el('feePlanAuditCsv').disabled = true;
    el('feePlanAuditPrint').disabled = true;
    try {
        const audit = await api('/plans/' + encodeURIComponent(reference) + '/billing-audit');
        if (!selected || selected.reference !== reference) return;
        billingAudit = audit;
        const findings = (audit.planIssues || []).join(' · ');
        el('feePlanAuditSummary').textContent =
            audit.studentCode + ' · ' + audit.months + ' months · ' +
            audit.billedMonths + ' reconciled · ' + audit.unbilledMonths + ' unbilled · ' +
            audit.reviewMonths + ' review · Contract ' + money(audit.contractGross) + ' ' +
            audit.currencyCode + ' · Verified invoice net ' + money(audit.verifiedInvoiceNet) +
            (findings ? ' · ' + findings : '');
        const body = document.createDocumentFragment();
        for (const row of audit.rows || []) {
            const tr = document.createElement('tr');
            const values = [row.monthStart, row.contractDueDate || '—', money(row.contractGross),
                row.invoiceNumber || '—', row.invoiceNet == null ? '—' : money(row.invoiceNet),
                row.invoicePaid == null ? '—' : money(row.invoicePaid),
                row.invoiceDue == null ? '—' : money(row.invoiceDue),
                row.status, (row.issues || []).join(' · ') || '—'];
            values.forEach((value, index) => {
                const td = cell(value);
                if ([2,4,5,6].includes(index)) td.className = 'text-end';
                tr.append(td);
            });
            body.append(tr);
        }
        el('feePlanAuditRows').replaceChildren(body);
        el('feePlanAudit').classList.remove('d-none');
        el('feePlanAuditCsv').disabled = !(audit.rows || []).length;
        el('feePlanAuditPrint').disabled = !(audit.rows || []).length;
    } catch (error) { notice(error.message, true); }
    finally { button.disabled = false; }
}
function exportBillingAudit() {
    if (!billingAudit?.rows?.length) return;
    const lines = [['Month','Contract due date','Contract gross','Invoice number',
        'Invoice gross','Invoice discount','Invoice net','Paid','Due','Journal linked',
        'Status','Review findings']];
    for (const x of billingAudit.rows) lines.push([x.monthStart,x.contractDueDate || '',
        money(x.contractGross),x.invoiceNumber || '',x.invoiceGross ?? '',
        x.invoiceDiscount ?? '',x.invoiceNet ?? '',x.invoicePaid ?? '',x.invoiceDue ?? '',
        x.journalLinked ? 'Yes - link only' : 'No',x.status,(x.issues || []).join('; ')]);
    const csv = '\uFEFF' + lines.map(x => x.map(csvCell).join(',')).join('\r\n') + '\r\n';
    const url = URL.createObjectURL(new Blob([csv], {type:'text/csv;charset=utf-8'}));
    const link = document.createElement('a');
    link.href = url; link.download = 'fee-plan-billing-audit.csv';
    document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
el('feePlanAuditOpen').addEventListener('click', openBillingAudit);
el('feePlanAuditCsv').addEventListener('click', exportBillingAudit);
el('feePlanAuditPrint').addEventListener('click', () => {
    if (!billingAudit?.rows?.length) return;
    root.classList.add('audit-print');
    window.print();
});
window.addEventListener('afterprint', () => root.classList.remove('audit-print'));

el('feePlanIssueMonth').addEventListener('change', () => { invoiceKey = null; });
el('feePlanIssue').addEventListener('click', async () => {
    if (!selected || selected.stateCode !== 3) return;
    const month = el('feePlanIssueMonth').value;
    if (!month) { notice('Choose an unbilled month.', true); return; }
    if (!invoiceKey || invoiceKey.month !== month || invoiceKey.plan !== selected.reference)
        invoiceKey = { month, plan: selected.reference, id: crypto.randomUUID() };
    const button = el('feePlanIssue'); button.disabled = true;
    try {
        const reference = selected.reference;
        const invoice = await api('/plans/' + encodeURIComponent(reference) + '/issue-month', 'POST', {
            clientRequestId: invoiceKey.id, monthStart: month, rowVersion: selected.rowVersion
        });
        invoiceKey = null;
        await detail(reference); await register();
        notice((invoice.existing ? 'Existing' : 'New') + ' invoice ' + invoice.invoiceNumber + ' recorded.');
    } catch (error) { notice(error.message, true); }
    finally { button.disabled = false; }
});
if (el('feePlanApprove')) el('feePlanApprove').addEventListener('click', () => transition('approve'));
if (el('feePlanCancel')) {
    const dialog = el('feePlanCancelDialog');
    el('feePlanCancel').addEventListener('click', () => {
        el('feePlanCancelReason').value = '';
        dialog.showModal();
        el('feePlanCancelReason').focus();
    });
    el('feePlanCancelDismiss').addEventListener('click', () => dialog.close());
    el('feePlanCancelForm').addEventListener('submit', event => {
        event.preventDefault();
        const reason = el('feePlanCancelReason').value.trim();
        if (!reason) return;
        dialog.close();
        transition('cancel', reason);
    });
}
const now = new Date();
el('feePlanMonth').value = now.getFullYear() + '-' + String(now.getMonth() + 1).padStart(2, '0');
api('/options').then(data => {
    options = data;
    el('feePlanBatch').replaceChildren(option('', 'Select academic batch'));
    for (const x of data.academicBatches || []) el('feePlanBatch').append(option(x.id, x.name));
    el('feePlanCampus').replaceChildren(option('', 'All authorized campuses'));
    for (const x of data.campuses || []) el('feePlanCampus').append(option(x.id, x.name));
    refreshStructures();
}).catch(error => notice(error.message, true));
register();
})();
