(() => {
'use strict';
if (!document.getElementById('feeReconciliation')) return;
const el = id => document.getElementById(id);
const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
const t = (en, bd) => bn ? bd : en;
let page = 1, pages = 1, active = null, generation = 0, controller = null;
const moneyKeys = /("(?:totalAmount|paidAmount|dueAmount|netAllocatedAmount|creditNoteAmount|reservationCreditAmount|studentCreditPayable)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
function money(value) {
    const raw = String(value ?? '0');
    if (!/^-?\d+(?:\.\d{1,2})?$/.test(raw)) throw Error(t('Invalid financial precision.', 'আর্থিক অঙ্কের দশমিক সঠিক নয়।'));
    const negative = raw.startsWith('-'), parts = (negative ? raw.slice(1) : raw).split('.');
    const n = BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2, '0'));
    return (negative ? '-' : '') + (n / 100n).toString() + '.' + String(n % 100n).padStart(2, '0');
}
function cell(value) { const td = document.createElement('td'); td.textContent = String(value ?? ''); return td; }
function status(value) {
    const labels = { Issued: 'ইস্যুকৃত', PartiallyPaid: 'আংশিক পরিশোধিত',
        Paid: 'পরিশোধিত', Successful: 'সফল', Refunded: 'ফেরত দেওয়া', Reconciled: 'সমন্বিত',
        Exception: 'অসংগতি', Matched: 'মিলেছে' };
    return bn && Object.prototype.hasOwnProperty.call(labels, value) ? labels[value] : value;
}
function current() { return { campusId: el('agingCampus').value, search: el('agingSearch').value.trim() }; }
function clear(message) {
    page = 1; pages = 1;
    el('reconcileRows').replaceChildren();
    el('reconcilePage').textContent = '—';
    el('reconcileNotice').textContent = message;
    el('reconcilePrev').disabled = true;
    el('reconcileNext').disabled = true;
}
function invalidate() {
    ++generation;
    if (controller) controller.abort();
    controller = null; active = null;
    clear(t('Filters changed. Check current filters again.', 'ফিল্টার বদলেছে। নতুন করে যাচাই করুন।'));
    el('reconcileRun').disabled = false;
}
async function load(next) {
    if (!active) return;
    const scope = { ...active }, token = ++generation;
    if (controller) controller.abort();
    controller = new AbortController();
    const button = el('reconcileRun');
    button.disabled = true;
    clear(t('Loading reconciliation…', 'সমন্বয় লোড হচ্ছে…'));
    try {
        const params = new URLSearchParams({ page: String(next), pageSize: '25' });
        if (scope.campusId) params.set('campusId', scope.campusId);
        if (scope.search) params.set('search', scope.search);
        const response = await fetch('/api/finance/receivables-aging/reconciliation?' + params,
            { credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
                headers: { Accept: 'application/json' } });
        if (response.status === 401 || response.status === 403)
            throw Error(t('Finance access denied. Check your campus access.', 'Finance অনুমতি নেই। ক্যাম্পাসের অনুমতি যাচাই করুন।'));
        if (!response.ok) throw Error(t('Reconciliation unavailable. Retry.', 'সমন্বয় পাওয়া যায়নি। আবার চেষ্টা করুন।'));
        const raw = await response.text();
        let result;
        try { result = JSON.parse(raw.replace(moneyKeys, '$1"$2"')); }
        catch { throw Error(t('Invalid reconciliation response.', 'সমন্বয়ের উত্তর সঠিক নয়।')); }
        if (!result?.success) throw Error(t('Reconciliation unavailable. Retry.', 'সমন্বয় পাওয়া যায়নি। আবার চেষ্টা করুন।'));
        if (token !== generation || JSON.stringify(scope) !== JSON.stringify(current())) return;
        const data = result.data;
        if (!data || data.page !== next || data.pageSize !== 25 ||
            !Number.isSafeInteger(data.totalCount) || data.totalCount < 0 ||
            !Number.isSafeInteger(data.pageExceptions) || data.pageExceptions < 0 ||
            !Array.isArray(data.rows) || data.rows.length > 25)
            throw Error(t('Reconciliation page mismatch.', 'সমন্বয়ের পৃষ্ঠা মিলছে না।'));
        const countPages = Math.max(1, Math.ceil(data.totalCount / 25));
        if (next > countPages || (data.totalCount > 0 && !data.rows.length) ||
            (next < countPages && data.rows.length !== 25))
            throw Error(t('Reconciliation page mismatch.', 'সমন্বয়ের পৃষ্ঠা মিলছে না।'));
        const body = el('reconcileRows');
        const fragment = document.createDocumentFragment();
        for (const x of data.rows) {
            const tr = document.createElement('tr');
            for (const value of [x.invoiceNumber, x.studentCode + ' — ' + x.studentName, x.campusName,
            money(x.totalAmount), money(x.paidAmount), money(x.netAllocatedAmount),
            money(x.creditNoteAmount), money(x.reservationCreditAmount), money(x.studentCreditPayable),
            money(x.dueAmount), status(x.status), x.issues || '—']) tr.append(cell(value));
            fragment.append(tr);
        }
        if (token !== generation) return;
        body.replaceChildren(fragment);
        if (!data.rows.length) {
            const tr = document.createElement('tr'), td = cell(t('No matching invoices.',
                'মিল পাওয়া ইনভয়েস নেই।'));
            td.colSpan = 12; tr.append(td); body.append(tr);
        }
        page = next; pages = countPages;
        el('reconcilePage').textContent = t('Page ', 'পৃষ্ঠা ') + page + t(' of ', ' / ') + pages +
            ' · ' + data.pageExceptions + t(' exception(s) on this page', 'টি অসংগতি এই পৃষ্ঠায়');
        el('reconcileNotice').textContent = t('Read-only. ', 'শুধু দেখার জন্য। ') + data.totalCount +
            t(' matching invoices; exceptions cover this page only.',
                'টি মিল পাওয়া ইনভয়েস; অসংগতি শুধু এই পৃষ্ঠার।');
        el('reconcilePrev').disabled = page <= 1;
        el('reconcileNext').disabled = page >= pages;
    } catch (error) {
        if (token === generation) {
            clear(error.message || t('Reconciliation unavailable.', 'সমন্বয় পাওয়া যায়নি।'));
        }
    } finally {
        if (token === generation) button.disabled = false;
    }
}
el('reconcileRun').addEventListener('click', () => { active = current(); void load(1); });
el('reconcilePrev').addEventListener('click', () => { if (active && page > 1) void load(page - 1); });
el('reconcileNext').addEventListener('click', () => { if (active && page < pages) void load(page + 1); });
for (const id of ['agingDate', 'agingCampus', 'agingSearch', 'agingPageSize']) {
    el(id).addEventListener('input', invalidate);
    el(id).addEventListener('change', invalidate);
}
clear(t('Run reconciliation to inspect invoices.', 'সমন্বয় চালিয়ে ইনভয়েস দেখুন।'));
})();