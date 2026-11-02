(() => {
'use strict';
const root = document.getElementById('feeAgingApp');
if (!root) return;
const el = id => document.getElementById(id);
const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
const t = (en, bd) => bn ? bd : en;
const state = { active: null, report: null, page: 1, pages: 1, generation: 0 };
let controller = null;
const moneyKeys = /("(?:totalAmount|paidAmount|dueAmount|outstanding|current|days1To30|days31To60|days61To90|daysOver90)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
function cents(value) {
    const raw = String(value ?? '0');
    if (!/^-?\d+(?:\.\d{1,2})?$/.test(raw)) throw Error(t('Invalid financial precision.', 'আর্থিক অঙ্কের দশমিক সঠিক নয়।'));
    const negative = raw.startsWith('-'), parts = (negative ? raw.slice(1) : raw).split('.');
    const n = BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2, '0'));
    return negative ? -n : n;
}
function money(value) {
    const n = cents(value), a = n < 0n ? -n : n;
    return (n < 0n ? '-' : '') + (a / 100n).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',') +
        '.' + String(a % 100n).padStart(2, '0');
}
function notice(message, error = false) {
    const node = el('agingAlert');
    node.className = 'alert ' + (error ? 'alert-danger' : 'alert-info') + ' d-print-none';
    node.textContent = message;
}
function cell(value) {
    const td = document.createElement('td');
    td.textContent = String(value ?? '');
    return td;
}
function empty(id, span, message) {
    const tr = document.createElement('tr'), td = cell(message);
    td.colSpan = span; td.className = 'text-center text-muted p-3';
    tr.append(td); el(id).replaceChildren(tr);
}
function validDate(value) {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
    const d = new Date(value + 'T00:00:00Z');
    return !Number.isNaN(d.getTime()) && d.toISOString().slice(0, 10) === value &&
        Number(value.slice(0, 4)) >= 2000 && Number(value.slice(0, 4)) <= 2200;
}
function readFilters() {
    const agingDate = el('agingDate').value, campusId = el('agingCampus').value;
    const search = el('agingSearch').value.trim(), pageSize = Number(el('agingPageSize').value);
    if (!validDate(agingDate) || ![25, 50, 100].includes(pageSize) || search.length > 100 ||
        (campusId && !/^[1-9]\d*$/.test(campusId)))
        throw Error(t('Choose a valid reference date, campus and page size.', 'সঠিক রেফারেন্স তারিখ, ক্যাম্পাস ও সারির সংখ্যা বাছুন।'));
    return { agingDate, campusId, search, pageSize };
}
function clearReport(message) {
    state.report = null; state.page = 1; state.pages = 1;
    empty('agingTotals', 8, message);
    empty('agingRows', 11, message);
    el('agingCount').textContent = '';
    el('agingPage').textContent = '—';
    for (const id of ['agingExport', 'agingPrev', 'agingNext']) el(id).disabled = true;
}
function invalidate() {
    ++state.generation;
    if (controller) controller.abort();
    controller = null; state.active = null;
    el('agingRun').disabled = false;
    clearReport(t('Filters changed. Run report.', 'ফিল্টার বদলেছে। প্রতিবেদন চালান।'));
    notice(t('Filters changed. Run the report for the new scope.', 'ফিল্টার বদলেছে। নতুন পরিসরে প্রতিবেদন চালান।'));
}
async function request(page, scope, signal) {
    const params = new URLSearchParams({ agingDate: scope.agingDate,
        page: String(page), pageSize: String(scope.pageSize) });
    if (scope.campusId) params.set('campusId', scope.campusId);
    if (scope.search) params.set('search', scope.search);
    const response = await fetch('/api/finance/receivables-aging?' + params,
        { credentials: 'same-origin', cache: 'no-store', signal, headers: { Accept: 'application/json' } });
    if (response.status === 401 || response.status === 403)
        throw Error(t('Finance access denied. Check your campus access.', 'Finance অনুমতি নেই। ক্যাম্পাসের অনুমতি যাচাই করুন।'));
    if (!response.ok) throw Error(t('Report unavailable. Check filters and retry.', 'প্রতিবেদন পাওয়া যায়নি। ফিল্টার দেখে আবার চেষ্টা করুন।'));
    const raw = await response.text();
    let json;
    try { json = JSON.parse(raw.replace(moneyKeys, '$1"$2"')); }
    catch { throw Error(t('Invalid report response.', 'প্রতিবেদনের উত্তর সঠিক নয়।')); }
    if (!json?.success) throw Error(t('Report unavailable. Retry with authorized filters.', 'প্রতিবেদন পাওয়া যায়নি। অনুমোদিত ফিল্টার দিয়ে আবার চেষ্টা করুন।'));
    return json.data;
}
function validate(data, expectedPage, scope) {
    const page = data?.invoices;
    if (data?.agingDate !== scope.agingDate || !page ||
        page.page !== expectedPage || page.pageSize !== scope.pageSize ||
        !Number.isSafeInteger(page.totalCount) || page.totalCount < 0 ||
        !Array.isArray(page.items) || page.items.length > scope.pageSize ||
        !Array.isArray(data.totals) || !Array.isArray(data.campuses))
        throw Error(t('Report scope mismatch. Export blocked.', 'প্রতিবেদনের পরিসর মিলছে না। রপ্তানি বন্ধ।'));
    const pages = Math.max(1, Math.ceil(page.totalCount / scope.pageSize));
    if (expectedPage > pages || (page.totalCount > 0 && !page.items.length) ||
        (expectedPage < pages && page.items.length !== scope.pageSize))
        throw Error(t('Report page mismatch. Export blocked.', 'প্রতিবেদনের পৃষ্ঠা মিলছে না। রপ্তানি বন্ধ।'));
    if (scope.campusId && !data.campuses.some(c => String(c.id) === scope.campusId))
        throw Error(t('Campus access changed. Report blocked.', 'ক্যাম্পাসের অনুমতি বদলেছে। প্রতিবেদন বন্ধ।'));
    for (const total of data.totals) {
        if (!Number.isSafeInteger(total.invoiceCount) || total.invoiceCount < 0 ||
            cents(total.outstanding) !== cents(total.current) + cents(total.days1To30) +
            cents(total.days31To60) + cents(total.days61To90) + cents(total.daysOver90))
            throw Error(t('Aging totals do not reconcile.', 'বকেয়ার মোট হিসাব মিলছে না।'));
    }
    const references = new Set();
    for (const x of page.items) {
        if (!x.reference || references.has(x.reference) || !Number.isSafeInteger(x.daysOverdue) ||
            x.daysOverdue < 0 || cents(x.dueAmount) <= 0n ||
            cents(x.totalAmount) < 0n || cents(x.paidAmount) < 0n)
            throw Error(t('Invoice page evidence mismatch.', 'ইনভয়েস পৃষ্ঠার তথ্য মিলছে না।'));
        references.add(x.reference);
    }
    return pages;
}
function render(data, scope, pages) {
    const select = el('agingCampus');
    select.replaceChildren();
    const all = document.createElement('option'); all.value = '';
    all.textContent = t('All accessible campuses', 'অনুমোদিত সব ক্যাম্পাস'); select.append(all);
    for (const campus of data.campuses) {
        const option = document.createElement('option');
        option.value = String(campus.id); option.textContent = campus.name; select.append(option);
    }
    select.value = scope.campusId;
    el('agingTotals').replaceChildren();
    for (const x of data.totals) {
        const tr = document.createElement('tr');
        for (const v of [x.currencyCode, x.invoiceCount, money(x.current), money(x.days1To30),
            money(x.days31To60), money(x.days61To90), money(x.daysOver90), money(x.outstanding)])
            tr.append(cell(v));
        el('agingTotals').append(tr);
    }
    if (!data.totals.length) empty('agingTotals', 8, t('No outstanding invoices for these filters.', 'এই ফিল্টারে কোনো বকেয়া ইনভয়েস নেই।'));
    el('agingRows').replaceChildren();
    for (const x of data.invoices.items) {
        const tr = document.createElement('tr');
        for (const v of [x.invoiceNumber, x.studentCode + ' — ' + x.displayName, x.campusName,
            String(x.invoiceDate).slice(0, 10), String(x.dueDate).slice(0, 10), x.currencyCode,
            money(x.totalAmount), money(x.paidAmount), money(x.dueAmount), x.daysOverdue,
            x.agingBucket === 'Current' ? t('Current', 'চলতি') : x.agingBucket])
            tr.append(cell(v));
        el('agingRows').append(tr);
    }
    if (!data.invoices.items.length) empty('agingRows', 11, t('No invoices on this page.', 'এই পৃষ্ঠায় ইনভয়েস নেই।'));
    state.report = data; state.page = data.invoices.page; state.pages = pages;
    el('agingCount').textContent = data.invoices.totalCount + t(' outstanding invoices', 'টি বকেয়া ইনভয়েস');
    el('agingPage').textContent = t('Page ', 'পৃষ্ঠা ') + state.page + t(' of ', ' / ') + state.pages;
    el('agingPrev').disabled = state.page <= 1;
    el('agingNext').disabled = state.page >= state.pages;
    el('agingExport').disabled = !data.invoices.items.length;
}
async function load(page) {
    if (!state.active) return;
    const scope = { ...state.active }, generation = ++state.generation;
    if (controller) controller.abort();
    controller = new AbortController();
    clearReport(t('Loading report…', 'প্রতিবেদন লোড হচ্ছে…'));
    el('agingRun').disabled = true;
    try {
        const result = await request(page, scope, controller.signal);
        if (generation !== state.generation) return;
        const pages = validate(result, page, scope);
        if (generation !== state.generation) return;
        render(result, scope, pages);
        notice(t('Current unpaid totals cover all matches; CSV exports this page only.',
            'বর্তমান বকেয়ার মোট সব মিলের জন্য; CSV শুধু এই পৃষ্ঠার।'));
    } catch (error) {
        if (generation !== state.generation) return;
        clearReport(t('No verified report.', 'যাচাইকৃত প্রতিবেদন নেই।'));
        notice(error.message || t('Report failed.', 'প্রতিবেদন ব্যর্থ।'), true);
    } finally {
        if (generation === state.generation) el('agingRun').disabled = false;
    }
}
function csvField(value) {
    let text = String(value ?? '');
    if (/^[\s\u0000-\u001f]*[=+\-@]/.test(text)) text = "'" + text;
    return '"' + text.replace(/"/g, '""') + '"';
}
function exportPage() {
    if (!state.report || !state.active) return;
    let current;
    try { current = readFilters(); } catch { invalidate(); return; }
    if (JSON.stringify(current) !== JSON.stringify(state.active)) { invalidate(); return; }
    const rows = [[t('Invoice', 'ইনভয়েস'), t('Student code', 'শিক্ষার্থী কোড'),
        t('Student name', 'শিক্ষার্থীর নাম'), t('Campus', 'ক্যাম্পাস'),
        t('Invoice date', 'ইনভয়েসের তারিখ'), t('Due date', 'শেষ তারিখ'),
        t('Currency', 'মুদ্রা'), t('Total', 'মোট'), t('Paid', 'পরিশোধিত'),
        t('Outstanding', 'বকেয়া'), t('Days overdue', 'বিলম্বের দিন'),
        t('Bucket', 'সময়ভাগ')]];
    for (const x of state.report.invoices.items) rows.push([x.invoiceNumber, x.studentCode,
        x.displayName, x.campusName, x.invoiceDate, x.dueDate, x.currencyCode,
        money(x.totalAmount), money(x.paidAmount), money(x.dueAmount), x.daysOverdue,
        x.agingBucket === 'Current' ? t('Current', 'চলতি') : x.agingBucket]);
    const csv = '\uFEFF' + rows.map(r => r.map(csvField).join(',')).join('\r\n') + '\r\n';
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const link = document.createElement('a'); link.href = url;
    link.download = 'fee-receivables-aging-' + state.active.agingDate + '-page-' + state.page + '.csv';
    document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
el('agingFilters').addEventListener('submit', event => {
    event.preventDefault();
    try { state.active = readFilters(); void load(1); }
    catch (error) { invalidate(); notice(error.message, true); }
});
for (const id of ['agingDate', 'agingCampus', 'agingSearch', 'agingPageSize']) {
    el(id).addEventListener('input', invalidate);
    el(id).addEventListener('change', invalidate);
}
el('agingPrev').addEventListener('click', () => { if (state.report && state.page > 1) void load(state.page - 1); });
el('agingNext').addEventListener('click', () => { if (state.report && state.page < state.pages) void load(state.page + 1); });
el('agingExport').addEventListener('click', exportPage);
const now = new Date();
el('agingDate').value = [now.getFullYear(), String(now.getMonth() + 1).padStart(2, '0'),
    String(now.getDate()).padStart(2, '0')].join('-');
state.active = readFilters();
void load(1);
})();