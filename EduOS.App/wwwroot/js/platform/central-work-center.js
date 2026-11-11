(() => {
    'use strict';
    const root = document.getElementById('centralWorkCenter');
    if (!root) return;
    const byId = id => document.getElementById(id);
    const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
    const sourceRoutes = Object.freeze({
        ProcurementRequisition: '/ProcurementRequisitions/Index',
        ProcurementPurchaseOrder: '/ProcurementPurchaseOrders/Index',
        FacilityVisitorPolicy: '/FacilityVisitors/Index',
        AccountingJournal: '/AccountingJournals/Index'
    });
    const names = {
        ProcurementRequisition: bn ? 'ক্রয় চাহিদাপত্র' : 'Purchase requisition',
        ProcurementPurchaseOrder: bn ? 'ক্রয়াদেশ' : 'Purchase order',
        FacilityVisitorPolicy: bn ? 'ভিজিটর গোপনীয়তা নীতি' : 'Visitor privacy policy',
        AccountingJournal: bn ? 'হিসাবের জার্নাল অনুমোদন' : 'Journal approval'
    };
    let page = 1, lastPage = 1, request = 0, pending = null;
    let currentItems = [], currentScope = '', resultReady = false;
    const previous = byId('cwcPrevious'), next = byId('cwcNext');
    const source = byId('cwcSource'), pageSize = byId('cwcPageSize');
    let loading = false;
    function alertMessage(message, error = true) {
        const box = byId('cwcAlert');
        box.className = error ? 'alert alert-danger' : 'alert alert-info';
        box.textContent = message;
    }
    function cell(row, value) {
        const td = document.createElement('td');
        td.textContent = value == null ? '' : String(value);
        row.appendChild(td);
        return td;
    }
    function updateActions() {
        const available = resultReady && !loading;
        previous.disabled = !available || page <= 1;
        next.disabled = !available || page >= lastPage;
        const exportButton = byId('cwcExport');
        if (exportButton) exportButton.disabled = !available || currentItems.length === 0;
        const printButton = byId('cwcPrint');
        if (printButton) printButton.disabled = !available || currentItems.length === 0;
    }
    function render(data) {
        const body = byId('cwcRows');
        body.replaceChildren();
        const result = data.pending;
        currentItems = result.items.slice();
        resultReady = true;
        lastPage = Math.max(1, Math.ceil(result.totalCount / result.pageSize));
        page = result.page;
        byId('cwcScope').textContent = (bn ? 'অনুমোদিত উৎস: ' : 'Authorized sources: ') +
            [data.inventoryIncluded ? (bn ? 'ইনভেন্টরি' : 'Inventory') : null,
             data.facilitiesIncluded ? (bn ? 'ফ্যাসিলিটিজ' : 'Facilities') : null,
             data.financeIncluded ? (bn ? 'ফাইন্যান্স' : 'Finance') : null]
                .filter(Boolean).join(', ') || (bn ? 'অনুমোদিত কাজ নেই' : 'No entitled sources');
        byId('cwcPage').textContent = `${result.page} / ${lastPage} (${result.totalCount})`;
        currentScope = byId('cwcScope').textContent;
        updateActions();
        if (!result.items.length) {
            const empty = document.createElement('tr');
            const td = cell(empty, bn ? 'কোনো অপেক্ষমাণ কাজ নেই' : 'No pending work in this scope.');
            td.colSpan = 6;
            body.appendChild(empty);
        }
        for (const item of result.items) {
            const row = document.createElement('tr');
            cell(row, names[item.source] || item.source);
            cell(row, item.reference);
            cell(row, item.campusId == null ? (bn ? 'প্রতিষ্ঠানব্যাপী' : 'Institution-wide') : item.campusId);
            cell(row, item.state);
            const instant = new Date(item.submittedAtUtc);
            cell(row, Number.isFinite(instant.getTime()) ? instant.toLocaleString() : '');
            const action = document.createElement('td');
            if (sourceRoutes[item.source] === item.workspaceUrl) {
                const link = document.createElement('a');
                const params = new URLSearchParams();
                const sourceId = Number(item.sourceId);
                const campusId = Number(item.campusId);
                if (Number.isSafeInteger(sourceId) && sourceId > 0 &&
                    item.source !== 'FacilityVisitorPolicy' &&
                    item.source !== 'AccountingJournal') params.set('workId', String(sourceId));
                if (item.source === 'FacilityVisitorPolicy' &&
                    Number.isSafeInteger(campusId) && campusId > 0) params.set('campusId', String(campusId));
                if (item.source === 'AccountingJournal') {
                    if (typeof item.sourceReference !== 'string' ||
                        !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(item.sourceReference)) {
                        row.appendChild(action);
                        body.appendChild(row);
                        continue;
                    }
                    params.set('reference', item.sourceReference);
                }
                link.href = sourceRoutes[item.source] + (params.size ? '?' + params : '');
                link.className = 'btn btn-outline-primary btn-sm';
                link.textContent = bn ? 'খুলুন' : 'Open';
                action.appendChild(link);
            }
            row.appendChild(action);
            body.appendChild(row);
        }
    }
    async function load() {
        if (pending) pending.abort();
        const controller = new AbortController();
        pending = controller;
        const serial = ++request;
        const campus = byId('cwcCampus').value.trim();
        const params = new URLSearchParams({ page: String(page), pageSize: pageSize.value });
        if (source.value) params.set('source', source.value);
        loading = true;
        resultReady = false;
        currentItems = [];
        updateActions();
        byId('cwcPage').textContent = bn ? 'লোড হচ্ছে…' : 'Loading…';
        byId('cwcRows').replaceChildren();
        if (campus) params.set('campusId', campus);
        try {
            const response = await fetch('/api/platform/work-center/pending?' + params,
                { credentials: 'same-origin', cache: 'no-store', signal: controller.signal });
            const json = await response.json();
            if (!response.ok || !json.success || !json.data)
                throw new Error(json.message || (bn ? 'তালিকা লোড করা যায়নি' : 'Cannot load work list.'));
            if (request !== serial) return;
            byId('cwcAlert').className = 'd-none';
            const result = json.data.pending;
            if (!result || !Array.isArray(result.items) ||
                !Number.isSafeInteger(result.totalCount) || result.totalCount < 0 ||
                !Number.isSafeInteger(result.pageSize) || result.pageSize < 1 ||
                !Number.isSafeInteger(result.page) || result.page < 1 ||
                result.page !== page || result.pageSize !== Number(pageSize.value) ||
                result.items.length > result.pageSize)
                throw new Error(bn ? 'তালিকার তথ্য সঠিক নয়' : 'Invalid work list response.');
            if (page > 1 && result.totalCount === 0) {
                page = 1;
                load(); return;
            }
            if (result && result.totalCount > 0 && page > 1 &&
                page > Math.ceil(result.totalCount / result.pageSize)) {
                page = Math.ceil(result.totalCount / result.pageSize);
                load(); return;
            }
            loading = false;
            render(json.data);
        } catch (error) {
            if (error.name !== 'AbortError' && request === serial) {
                alertMessage(error.message || 'Unable to load.');
                byId('cwcPage').textContent = bn ? 'তালিকা পাওয়া যায়নি' : 'List unavailable';
                byId('cwcScope').textContent = '';
                resultReady = false;
                currentItems = [];
            }
        } finally {
            if (pending === controller) {
                pending = null;
                loading = false;
                updateActions();
            }
        }
    }
    // Export only the currently fetched, permission-scoped page; never infer or request unseen rows.
    // The file is a local snapshot, not a replacement for source-module financial reports.
    function exportCurrentPage() {
        if (!resultReady || loading || !currentItems.length) return;
        const safe = value => {
            let s = value == null ? '' : String(value);
            if (/^[\\s\\uFEFF]*[=+@\\-]/.test(s)) s = "'" + s;
            return '"' + s.replace(/"/g, '""') + '"';
        };
        const header = ['Module', 'Reference', 'Campus', 'State', 'Submitted UTC', 'Source'];
        const rows = currentItems.map(item => [
            names[item.source] || item.source, item.reference,
            item.campusId == null ? 'Institution-wide' : item.campusId,
            item.state, item.submittedAtUtc, item.source
        ]);
        const csv = '\uFEFF' + [header, ...rows].map(row => row.map(safe).join(',')).join('\r\n');
        const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = 'my-work-page-' + page + '.csv';
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        URL.revokeObjectURL(url);
    }
    const exportButton = byId('cwcExport');
    if (exportButton) exportButton.addEventListener('click', exportCurrentPage);
    const printButton = byId('cwcPrint');
    if (printButton) printButton.addEventListener('click', () => {
        if (resultReady && !loading && currentItems.length) window.print();
    });
    byId('cwcFilters').addEventListener('submit', event => {
        event.preventDefault(); page = 1; load();
    });
    source.addEventListener('change', () => { page = 1; load(); });
    pageSize.addEventListener('change', () => { page = 1; load(); });
    byId('cwcCampus').addEventListener('change', () => { page = 1; load(); });
    byId('cwcPrevious').addEventListener('click', () => {
        if (page > 1) { --page; load(); }
    });
    byId('cwcNext').addEventListener('click', () => {
        if (page < lastPage) { ++page; load(); }
    });
    window.addEventListener('pagehide', () => { if (pending) pending.abort(); });
    load();
})();
