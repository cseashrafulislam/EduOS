(() => {
'use strict';
const root = document.getElementById('feeDueCalendarApp');
if (!root) return;
const el = id => document.getElementById(id);
const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
const t = (en, bd) => bn ? bd : en;
const state = { filters: null, report: null, page: 1, pages: 1, generation: 0 };
let controller = null;
const moneyPattern = /("(?:outstanding|totalAmount|paidAmount|dueAmount)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
const iso = date => [date.getFullYear(), String(date.getMonth() + 1).padStart(2, '0'),
    String(date.getDate()).padStart(2, '0')].join('-');
const start = new Date(); start.setDate(1);
const end = new Date(start.getFullYear(), start.getMonth() + 3, 0);
el('feeDueFrom').value = iso(start);
el('feeDueTo').value = iso(end);
function cents(value) {
    const raw = String(value ?? '0');
    if (!/^-?\d+(?:\.\d{1,2})?$/.test(raw)) throw Error(t('Invalid financial precision.', 'আর্থিক অঙ্কের দশমিক সঠিক নয়।'));
    const negative = raw[0] === '-';
    const parts = (negative ? raw.slice(1) : raw).split('.');
    const n = BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2, '0'));
    return negative ? -n : n;
}
function money(value) {
    const n = cents(value), a = n < 0n ? -n : n;
    return (n < 0n ? '-' : '') + (a / 100n).toString() + '.' + String(a % 100n).padStart(2, '0');
}
function cell(value) {
    const td = document.createElement('td');
    td.textContent = String(value ?? '');
    return td;
}
function empty(id, colspan, message) {
    const row = document.createElement('tr'), td = cell(message);
    td.colSpan = colspan; td.className = 'text-center text-muted p-3';
    row.append(td); el(id).replaceChildren(row);
}
function notice(message, error) {
    const node = el('feeDueNotice');
    node.className = 'alert ' + (error ? 'alert-danger' : 'alert-success') + ' d-print-none';
    node.textContent = message;
}
function validDate(s) {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(s)) return false;
    const d = new Date(s + 'T00:00:00Z');
    return !Number.isNaN(d.getTime()) && d.toISOString().slice(0, 10) === s &&
        Number(s.slice(0, 4)) >= 2000 && Number(s.slice(0, 4)) <= 2200;
}
function filters() {
    const fromDate = el('feeDueFrom').value, toDate = el('feeDueTo').value;
    const pageSize = Number(el('feeDueSize').value);
    const days = (Date.parse(toDate + 'T00:00:00Z') - Date.parse(fromDate + 'T00:00:00Z')) / 86400000;
    if (!validDate(fromDate) || !validDate(toDate) || days < 0 || days > 366 ||
        ![25, 50, 100].includes(pageSize) || el('feeDueSearch').value.trim().length > 100)
        throw Error(t('Select valid due dates (up to 366 days) and page size.', 'সঠিক পরিশোধের তারিখ (সর্বোচ্চ ৩৬৬ দিন) ও সারির সংখ্যা বাছুন।'));
    return { fromDate, toDate, campusId: el('feeDueCampus').value,
        search: el('feeDueSearch').value.trim(), pageSize };
}
function clearReport(message) {
    state.report = null; state.page = 1; state.pages = 1;
    empty('feeDueMonths', 4, message);
    empty('feeDueRows', 8, message);
    el('feeDueScope').textContent = t('Current unpaid issued invoices', 'ইস্যুকৃত ইনভয়েসের বর্তমান বকেয়া');
    el('feeDueCount').textContent = '';
    el('feeDuePage').textContent = '—';
    for (const id of ['feeDueCsv', 'feeDuePrint', 'feeDuePrev', 'feeDueNext']) el(id).disabled = true;
}
function invalidate() {
    ++state.generation;
    if (controller) controller.abort();
    controller = null;
    state.filters = null;
    el('feeDueRun').disabled = false;
    clearReport(t('Filters changed. Run report.', 'ফিল্টার বদলেছে। প্রতিবেদন চালান।'));
    notice(t('Filters changed. Run the report to verify this scope.', 'ফিল্টার বদলেছে। এই পরিসর যাচাই করতে প্রতিবেদন চালান।'), false);
}
async function fetchPage(page, scope, signal) {
    const p = new URLSearchParams({ fromDate: scope.fromDate, toDate: scope.toDate,
        page: String(page), pageSize: String(scope.pageSize) });
    if (scope.campusId) p.set('campusId', scope.campusId);
    if (scope.search) p.set('search', scope.search);
    const response = await fetch('/api/finance/receivables-aging/due-calendar?' + p,
        { credentials: 'same-origin', cache: 'no-store', signal,
            headers: { Accept: 'application/json' } });
    if (response.status === 401 || response.status === 403)
        throw Error(t('Finance access denied. Sign in again or check your campus access.', 'Finance অনুমতি নেই। আবার সাইন ইন করুন বা ক্যাম্পাসের অনুমতি যাচাই করুন।'));
    if (!response.ok)
        throw Error(t('Report unavailable. Check your access and retry.', 'প্রতিবেদন পাওয়া যায়নি। অনুমতি যাচাই করে আবার চেষ্টা করুন।'));
    const raw = await response.text();
    let result;
    try { result = JSON.parse(raw.replace(moneyPattern, '$1"$2"')); }
    catch { throw Error(t('Invalid report response.', 'প্রতিবেদনের উত্তর সঠিক নয়।')); }
    if (!result?.success) throw Error(t('Report unavailable. Review filters and retry.', 'প্রতিবেদন পাওয়া যায়নি। ফিল্টার দেখে আবার চেষ্টা করুন।'));
    return result.data;
}
function validate(data, page, scope) {
    const p = data?.invoices;
    if (data?.fromDate !== scope.fromDate || data?.toDate !== scope.toDate ||
        !p || p.page !== page || p.pageSize !== scope.pageSize ||
        !Number.isSafeInteger(p.totalCount) || p.totalCount < 0 ||
        !Array.isArray(p.items) || p.items.length > scope.pageSize ||
        !Array.isArray(data.months) || !Array.isArray(data.campuses))
        throw Error(t('Report scope mismatch. Export blocked.', 'প্রতিবেদনের পরিসর মিলছে না। এক্সপোর্ট বন্ধ।'));
    const pages = Math.max(1, Math.ceil(p.totalCount / scope.pageSize));
    if (page > pages || (p.totalCount > 0 && !p.items.length))
        throw Error(t('Report page mismatch. Export blocked.', 'প্রতিবেদনের পৃষ্ঠা মিলছে না। এক্সপোর্ট বন্ধ।'));
    for (const m of data.months) {
        if (!Number.isInteger(m.year) || !Number.isInteger(m.month) || m.month < 1 || m.month > 12 ||
            !Number.isInteger(m.invoiceCount) || m.invoiceCount < 0 || cents(m.outstanding) < 0n)
            throw Error(t('Monthly totals are invalid. Export blocked.', 'মাসভিত্তিক মোট সঠিক নয়। এক্সপোর্ট বন্ধ।'));
    }
    for (const x of p.items) {
        if (!validDate(String(x.dueDate).slice(0, 10)) ||
            String(x.dueDate).slice(0, 10) < scope.fromDate ||
            String(x.dueDate).slice(0, 10) > scope.toDate ||
            cents(x.dueAmount) < 0n || cents(x.totalAmount) < 0n ||
            cents(x.paidAmount) < 0n ||
            cents(x.totalAmount) - cents(x.paidAmount) < cents(x.dueAmount))
            throw Error(t('Invoice receivable mismatch. Export blocked.', 'ইনভয়েসের প্রাপ্য অর্থ মিলছে না। এক্সপোর্ট বন্ধ।'));
    }
    return pages;
}
function render(data, scope, pages) {
    const selected = scope.campusId;
    el('feeDueCampus').replaceChildren();
    const all = document.createElement('option'); all.value = '';
    all.textContent = t('All accessible', 'অনুমোদিত সব ক্যাম্পাস');
    el('feeDueCampus').append(all);
    for (const c of data.campuses) {
        const option = document.createElement('option');
        option.value = String(c.id); option.textContent = c.name;
        el('feeDueCampus').append(option);
    }
    if (selected && data.campuses.some(c => String(c.id) === selected))
        el('feeDueCampus').value = selected;
    else if (selected) throw Error(t('Campus access changed. Reload the report.', 'ক্যাম্পাসের অনুমতি বদলেছে। প্রতিবেদন আবার চালান।'));
    const monthRows = document.createDocumentFragment();
    for (const m of data.months) {
        const tr = document.createElement('tr');
        for (const v of [String(m.year) + '-' + String(m.month).padStart(2, '0'),
            m.currencyCode, m.invoiceCount, money(m.outstanding)]) tr.append(cell(v));
        monthRows.append(tr);
    }
    el('feeDueMonths').replaceChildren(monthRows);
    if (!data.months.length) empty('feeDueMonths', 4, t('No unpaid issued invoices in this range.', 'এই সময়সীমায় ইস্যুকৃত কোনো বকেয়া ইনভয়েস নেই।'));
    const rows = document.createDocumentFragment();
    for (const x of data.invoices.items) {
        const tr = document.createElement('tr');
        for (const v of [String(x.dueDate).slice(0, 10), x.invoiceNumber,
            x.studentCode + ' — ' + x.studentName, x.campusName, x.currencyCode,
            money(x.totalAmount), money(x.paidAmount), money(x.dueAmount)]) tr.append(cell(v));
        rows.append(tr);
    }
    el('feeDueRows').replaceChildren(rows);
    if (!data.invoices.items.length) empty('feeDueRows', 8, t('No invoices on this page.', 'এই পৃষ্ঠায় কোনো ইনভয়েস নেই।'));
    state.report = data; state.page = data.invoices.page; state.pages = pages;
    el('feeDueScope').textContent = t('Current unpaid invoices due ', 'বর্তমান বকেয়া ইনভয়েস, শেষ তারিখ ') +
        data.fromDate + t(' to ', ' থেকে ') + data.toDate;
    el('feeDueCount').textContent = String(data.invoices.totalCount) + t(' invoices', 'টি ইনভয়েস');
    el('feeDuePage').textContent = t('Page ', 'পৃষ্ঠা ') + state.page + t(' of ', ' / ') + state.pages;
    el('feeDuePrev').disabled = state.page <= 1;
    el('feeDueNext').disabled = state.page >= state.pages;
    el('feeDueCsv').disabled = !data.invoices.items.length;
    el('feeDuePrint').disabled = !data.invoices.items.length;
}
async function load(page) {
    if (!state.filters) return;
    if (controller) controller.abort();
    controller = new AbortController();
    const current = controller, generation = ++state.generation, scope = { ...state.filters };
    el('feeDueRun').disabled = true;
    clearReport(t('Loading verified report…', 'যাচাইকৃত প্রতিবেদন লোড হচ্ছে…'));
    try {
        const data = await fetchPage(page, scope, current.signal);
        if (generation !== state.generation || current.signal.aborted) return;
        const pages = validate(data, page, scope);
        if (generation !== state.generation || current.signal.aborted) return;
        render(data, scope, pages);
        notice(t('Report loaded.', 'প্রতিবেদন লোড হয়েছে।'), false);
    } catch (error) {
        if (generation !== state.generation || current.signal.aborted) return;
        clearReport(t('No verified report loaded.', 'কোনো যাচাইকৃত প্রতিবেদন লোড হয়নি।'));
        notice(error.message || t('Report failed.', 'প্রতিবেদন ব্যর্থ হয়েছে।'), true);
    } finally {
        if (generation === state.generation) el('feeDueRun').disabled = false;
    }
}
function csvCell(value) {
    let s = String(value ?? '');
    if (/^[\s\u0000-\u001f]*[=+\-@]/.test(s)) s = "'" + s;
    return '"' + s.replace(/"/g, '""') + '"';
}
function exportCsv() {
    if (!state.report || !state.filters) return;
    const rows = [[t('Due date', 'শেষ তারিখ'), t('Invoice', 'ইনভয়েস'),
        t('Student code', 'শিক্ষার্থী কোড'), t('Student name', 'শিক্ষার্থীর নাম'),
        t('Campus', 'ক্যাম্পাস'), t('Currency', 'মুদ্রা'), t('Total', 'মোট'),
        t('Paid', 'পরিশোধিত'), t('Due', 'বকেয়া')]];
    for (const x of state.report.invoices.items) rows.push([x.dueDate, x.invoiceNumber,
        x.studentCode, x.studentName, x.campusName, x.currencyCode,
        money(x.totalAmount), money(x.paidAmount), money(x.dueAmount)]);
    const blob = new Blob(['\uFEFF' + rows.map(r => r.map(csvCell).join(',')).join('\r\n')],
        { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob), link = document.createElement('a');
    link.href = url; link.download = 'student-fee-due-calendar-page-' + state.page + '.csv';
    document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
el('feeDueFilters').addEventListener('input', invalidate);
el('feeDueFilters').addEventListener('change', invalidate);
el('feeDueFilters').addEventListener('submit', event => {
    event.preventDefault();
    try { state.filters = filters(); load(1); }
    catch (error) { invalidate(); notice(error.message, true); }
});
el('feeDuePrev').addEventListener('click', () => { if (state.filters && state.page > 1) load(state.page - 1); });
el('feeDueNext').addEventListener('click', () => { if (state.filters && state.page < state.pages) load(state.page + 1); });
el('feeDueCsv').addEventListener('click', exportCsv);
el('feeDuePrint').addEventListener('click', () => { if (state.report && state.filters) window.print(); });
})();