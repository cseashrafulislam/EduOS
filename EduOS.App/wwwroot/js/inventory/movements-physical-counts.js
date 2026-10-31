import { api, el, td, fill, catalogs, activeLocations, notice, root } from './movements-api.js';

export const validQuantity = value => /^\d{1,12}(?:\.\d{1,6})?$/.test(String(value)) &&
    Number.isFinite(Number(value)) && Number(value) >= 0 && Number(value) <= 999999999999;
export const validSignedQuantity = value => /^-?\d{1,12}(?:\.\d{1,6})?$/.test(String(value)) &&
    Number.isFinite(Number(value)) && Math.abs(Number(value)) <= 999999999999;
export const validUnitCost = value => /^\d{1,12}(?:\.\d{1,4})?$/.test(String(value)) &&
    Number(value) <= 999999999999;

const make = (tag, className, label) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (label != null) node.textContent = label;
    return node;
};
const date = value => value ? new Date(value).toLocaleString() : '—';
const quantity = value => value == null ? '—' : String(value);

function exportCountCsv(count) {
    const header = ['Count UUID', 'State', 'Location ID', 'Snapshot UTC', 'Blind', 'Reason',
        'Line', 'Item ID', 'Lot', 'Book qty', 'Book value', 'Reserved qty', 'Counted qty',
        'Suggested variance', 'Posted variance', 'Posted unit cost', 'Correction reason', 'Posted movement UUID'];
    const csvSafe = value => {
        const raw = value == null ? '' : String(value);
        // Avoid spreadsheet formula execution when a user-controlled lot or reason begins with a formula.
        const safe = typeof value === 'number' || typeof value === 'boolean' ? raw :
            /^[=+\-@\t\r]/.test(raw) ? "'" + raw : raw;
        return '"' + safe.replaceAll('"', '""') + '"';
    };
    const data = count.lines.map(line => [count.reference, count.stateCode, count.locationId,
        count.snapshotAtUtc, count.blind, count.reason, line.lineNo, line.inventoryItemId,
        line.lotOrBatchNo, line.bookQuantity, line.bookValue, line.reservedQuantity,
        line.countedQuantity, line.suggestedVariance, line.postedVariance, line.postedUnitCost,
        line.correctionReason, line.postedMovementReference]);
    const blob = new Blob(['\ufeff', [header, ...data].map(row => row.map(csvSafe).join(',')).join('\r\n')],
        { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = make('a');
    link.href = url; link.download = 'physical-count-' + count.reference + '.csv';
    document.body.append(link); link.click(); link.remove();
    URL.revokeObjectURL(url);
}

export async function initPhysicalCounts(refreshLedger = () => {}) {
    if (!el('physicalCounts')) return;
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const canCreate = root.dataset.create === 'true';
    const canReview = root.dataset.post === 'true';
    const currentUserId = Number(root.dataset.userId);
    let page = 1, last = 1, selected = null, loadSequence = 0;
    let startRequestId = crypto.randomUUID(), submitRequestId = crypto.randomUUID(), reviewRequestId = crypto.randomUUID();
    const items = [], locationNames = new Map();
    const locations = await activeLocations();
    locations.forEach(location => locationNames.set(location.id, location.code + ' — ' + location.name));
    fill(el('countLocationFilter'), locations, bn ? 'সব স্টোর' : 'All stores');
    el('countHelp')?.addEventListener('click', () => {
        const opened = el('countHelpText').classList.toggle('d-none') === false;
        el('countHelp').setAttribute('aria-expanded', String(opened));
    });

    async function load(preferredReference = null) {
        const generation = ++loadSequence;
        const params = new URLSearchParams({ page: String(page), pageSize: '25' });
        if (el('countStateFilter').value) params.set('state', el('countStateFilter').value);
        if (el('countLocationFilter').value) params.set('locationId', el('countLocationFilter').value);
        const result = await api('movements/counts?' + params);
        if (generation !== loadSequence) return;
        last = Math.max(1, result.totalPages);
        el('countPrevious').disabled = page <= 1;
        el('countNext').disabled = page >= last;
        el('countPageLabel').textContent = (bn ? 'পৃষ্ঠা ' : 'Page ') + page + ' / ' + last + ' · ' + result.totalCount;
        const tbody = el('countRegister'); tbody.replaceChildren();
        for (const count of result.items) {
            const row = make('tr');
            [date(count.snapshotAtUtc), locationNames.get(count.locationId) || count.locationId,
                count.stateCode, count.lines.length].forEach(value => row.append(td(value)));
            const cell = td(''), button = make('button', 'btn btn-outline-primary btn-sm', bn ? 'দেখুন' : 'Open');
            button.type = 'button';
            button.addEventListener('click', () => show(count.reference).catch(error => notice(error.message)));
            cell.append(button); row.append(cell); tbody.append(row);
        }
        if (!result.items.length) {
            const row = make('tr'), cell = td(bn ? 'কোনো গণনা নেই।' : 'No counts found.');
            cell.colSpan = 5; row.append(cell); tbody.append(row);
        }
        if (preferredReference) await show(preferredReference);
        else if (selected) {
            const inPage = result.items.some(count => count.reference === selected.reference);
            if (inPage) await show(selected.reference);
            else { selected = null; el('countDetail').classList.add('d-none'); }
        }
    }

    function reviewFields(count) {
        const section = el('countCorrections'), wrapper = el('countCorrectionsContainer');
        if (!section) return;
        section.replaceChildren();
        const mode = el('countVarianceMode').value, approving = el('countDecision').value === 'Approve';
        wrapper.classList.toggle('d-none', !approving);
        el('countVarianceMode').disabled = !approving;
        if (!approving) return;
        for (const line of count.lines) {
            const row = make('div', 'row g-1 border-bottom py-2');
            const identity = make('div', 'col-lg-3 small', `${line.inventoryItemId} / ${line.lotOrBatchNo || '—'} · ${bn ? 'প্রস্তাবিত' : 'Suggested'} ${quantity(line.suggestedVariance)}`);
            const quantityWrap = make('div', 'col-lg-2'), input = make('input', 'form-control form-control-sm');
            input.type = 'text'; input.inputMode = 'decimal'; input.required = mode === 'ManualFinal';
            input.dataset.countLine = String(line.lineNo); input.setAttribute('aria-label', 'Signed variance for line ' + line.lineNo);
            input.value = mode === 'ManualFinal' ? String(line.suggestedVariance) : '0';
            if (mode === 'Automatic') { input.classList.add('d-none'); input.disabled = true; }
            quantityWrap.append(input);
            const costWrap = make('div', 'col-lg-2'), cost = make('input', 'form-control form-control-sm');
            cost.type = 'text'; cost.inputMode = 'decimal'; cost.placeholder = bn ? 'একক মূল্য' : 'Unit cost';
            cost.dataset.countCost = String(line.lineNo); cost.setAttribute('aria-label', 'Surplus unit cost line ' + line.lineNo);
            costWrap.append(cost);
            const reasonWrap = make('div', 'col-lg-5'), reason = make('input', 'form-control form-control-sm');
            reason.placeholder = bn ? 'সংশোধনের কারণ (প্রয়োজনে)' : 'Correction reason when variance differs';
            reason.maxLength = 800; reason.dataset.countReason = String(line.lineNo);
            reason.setAttribute('aria-label', 'Correction reason line ' + line.lineNo);
            reasonWrap.append(reason);
            row.append(identity, quantityWrap, costWrap, reasonWrap); section.append(row);
        }
    }

    async function show(reference) {
        const count = await api('movements/counts/' + reference);
        selected = count;
        el('countDetail').classList.remove('d-none');
        el('countDetailHeading').textContent = count.stateCode + ' · ' + count.reference;
        el('countDetailMetadata').textContent = `${locationNames.get(count.locationId) || count.locationId} · ` +
            `${bn ? 'কারণ' : 'Reason'}: ${count.reason} · ${bn ? 'স্ন্যাপশট' : 'Snapshot'}: ${date(count.snapshotAtUtc)} · ` +
            `${bn ? 'তৈরি' : 'Maker'}: ${count.createdByUserId || '—'} · ${bn ? 'রিভিউ' : 'Checker'}: ${count.reviewedByUserId || '—'} ` +
            (count.varianceMode ? ' · ' + count.varianceMode : '') + (count.reviewReason ? ' · ' + count.reviewReason : '');
        const draft = count.stateCode === 'Draft';
        el('countSubmitForm').classList.toggle('d-none', !draft || !canCreate || count.createdByUserId !== currentUserId);
        el('countAuditPanel').classList.toggle('d-none', draft);
        if (draft) {
            const rows = el('countDraftLines'); rows.replaceChildren();
            for (const line of count.lines) {
                const row = make('tr'), input = make('input', 'form-control form-control-sm');
                input.type = 'text'; input.inputMode = 'decimal'; input.required = true;
                input.placeholder = '0'; input.dataset.countMeasuredLine = String(line.lineNo);
                input.setAttribute('aria-label', 'Counted quantity line ' + line.lineNo);
                row.append(td(`${line.inventoryItemId} / ${line.lotOrBatchNo || '—'}`), td(quantity(line.bookQuantity)));
                const cell = td(''); cell.append(input); row.append(cell); rows.append(row);
            }
        } else {
            const rows = el('countAuditLines'); rows.replaceChildren();
            for (const line of count.lines) {
                const row = make('tr');
                [`${line.inventoryItemId} / ${line.lotOrBatchNo || '—'}`, quantity(line.bookQuantity),
                    quantity(line.countedQuantity), quantity(line.suggestedVariance),
                    quantity(line.postedVariance), line.postedMovementReference || '—']
                    .forEach(value => row.append(td(value)));
                rows.append(row);
            }
            const reviewer = el('countReviewForm');
            if (reviewer) reviewer.classList.toggle('d-none', count.stateCode !== 'Submitted' ||
                !canReview || count.createdByUserId === currentUserId);
            if (reviewer && count.stateCode === 'Submitted') reviewFields(count);
        }
    }

    el('countExportCsv').addEventListener('click', () => { if (selected) exportCountCsv(selected); });
    el('countPrint').addEventListener('click', () => {
        if (!selected) return;
        window.open('/InventoryMovements/PrintPhysicalCount?reference=' + encodeURIComponent(selected.reference),
            '_blank', 'noopener,noreferrer');
    });
    el('countReload').addEventListener('click', () => load().catch(error => notice(error.message)));
    el('countPrevious').addEventListener('click', () => { if (page > 1) { page--; load().catch(error => notice(error.message)); } });
    el('countNext').addEventListener('click', () => { if (page < last) { page++; load().catch(error => notice(error.message)); } });
    [el('countStateFilter'), el('countLocationFilter')].forEach(select => select.addEventListener('change', () => {
        page = 1; load().catch(error => notice(error.message));
    }));

    if (canCreate && el('countStartForm')) {
        fill(el('countLocation'), locations, bn ? 'স্টোর বাছুন' : 'Select store');
        const first = await catalogs('', locations);
        fill(el('countItem'), first.items, bn ? 'পণ্য বাছুন' : 'Select item');
        let debounce;
        el('countItemSearch').addEventListener('input', () => {
            clearTimeout(debounce);
            debounce = setTimeout(async () => {
                try {
                    const found = await catalogs(el('countItemSearch').value.trim(), locations);
                    fill(el('countItem'), found.items, bn ? 'পণ্য বাছুন' : 'Select item');
                } catch (error) { notice(error.message); }
            }, 350);
        });
        function drawItems() {
            const list = el('countSelectedLines'); list.replaceChildren();
            items.forEach((item, index) => {
                const li = make('li', 'list-group-item d-flex justify-content-between align-items-center',
                    `${item.inventoryItemId} · ${item.label} / ${item.lotOrBatchNo || '—'}`);
                const remove = make('button', 'btn btn-outline-danger btn-sm', bn ? 'বাদ' : 'Remove');
                remove.type = 'button'; remove.addEventListener('click', () => { items.splice(index, 1); drawItems(); });
                li.append(remove); list.append(li);
            });
        }
        el('countAddLine').addEventListener('click', () => {
            const itemId = Number(el('countItem').value), lot = el('countLot').value.trim();
            if (!itemId || items.length >= 100 || items.some(x => x.inventoryItemId === itemId && x.lotOrBatchNo === lot)) {
                notice(bn ? 'একই পণ্য/লট দুবার যোগ করা যাবে না (সর্বোচ্চ ১০০)।' : 'Select a unique item/lot (maximum 100).'); return;
            }
            items.push({ inventoryItemId: itemId, lotOrBatchNo: lot,
                label: el('countItem').selectedOptions[0]?.textContent || String(itemId) });
            drawItems(); el('countLot').value = '';
        });
        el('countStartForm').addEventListener('submit', async event => {
            event.preventDefault();
            if (!items.length) { notice(bn ? 'আগে অন্তত একটি পণ্য যুক্ত করুন।' : 'Add at least one item.'); return; }
            if (!el('countStartForm').reportValidity()) return;
            const button = el('countStart'); button.disabled = true;
            try {
                const count = await api('movements/counts', 'POST', {
                    clientRequestId: startRequestId, locationId: Number(el('countLocation').value),
                    blind: el('countBlind').checked, reason: el('countStartReason').value.trim(),
                    lines: items.map(x => ({ inventoryItemId: x.inventoryItemId, lotOrBatchNo: x.lotOrBatchNo }))
                });
                startRequestId = crypto.randomUUID(); submitRequestId = crypto.randomUUID();
                items.length = 0; drawItems(); el('countStartReason').value = '';
                el('countLocationFilter').value = ''; el('countStateFilter').value = ''; page = 1;
                notice(bn ? 'গণনা খোলা হয়েছে।' : 'Count opened.', true);
                await load(count.reference);
            } catch (error) { notice(error.message); }
            finally { button.disabled = false; }
        });
        el('countSubmitForm').addEventListener('submit', async event => {
            event.preventDefault(); if (!selected || selected.stateCode !== 'Draft') return;
            const measured = [...el('countDraftLines').querySelectorAll('[data-count-measured-line]')];
            if (!measured.every(x => validQuantity(x.value.trim()))) {
                notice(bn ? 'সব লাইনে ০ সহ বৈধ পরিমাণ দিন।' : 'Enter an explicit valid quantity on every line, including zero.'); return;
            }
            const button = el('countSubmit'); button.disabled = true;
            try {
                const count = await api('movements/counts/' + selected.reference + '/submit', 'POST', {
                    clientRequestId: submitRequestId, rowVersion: selected.rowVersion,
                    lines: measured.map(x => ({ lineNo: Number(x.dataset.countMeasuredLine), countedQuantity: Number(x.value) }))
                });
                submitRequestId = crypto.randomUUID(); reviewRequestId = crypto.randomUUID();
                notice(bn ? 'গণনা অনুমোদনের জন্য জমা হয়েছে।' : 'Count submitted for approval.', true);
                await load(count.reference);
            } catch (error) { notice(error.message); }
            finally { button.disabled = false; }
        });
    }

    if (canReview && el('countReviewForm')) {
        el('countVarianceMode').addEventListener('change', () => { if (selected) reviewFields(selected); });
        el('countDecision').addEventListener('change', () => { if (selected) reviewFields(selected); });
        el('countReviewForm').addEventListener('submit', async event => {
            event.preventDefault(); if (!selected || selected.stateCode !== 'Submitted') return;
            const mode = el('countVarianceMode').value, decision = el('countDecision').value;
            const corrections = [];
            if (decision === 'Approve') {
                for (const field of el('countCorrections').querySelectorAll('[data-count-line]')) {
                    const lineNo = Number(field.dataset.countLine), raw = field.value.trim();
                    const costRaw = el('countCorrections').querySelector(`[data-count-cost="${lineNo}"]`).value.trim();
                    const reason = el('countCorrections').querySelector(`[data-count-reason="${lineNo}"]`).value.trim();
                    if (mode !== 'Automatic' && (mode === 'ManualFinal' || raw) && !validSignedQuantity(raw)) {
                        notice(bn ? 'পার্থক্যের সঠিক সংখ্যা দিন।' : 'Enter a valid signed variance.'); return;
                    }
                    if (costRaw && !validUnitCost(costRaw)) {
                        notice(bn ? 'একক মূল্যের সঠিক সংখ্যা দিন।' : 'Enter a valid unit cost.'); return;
                    }
                    const parsed = raw ? Number(raw) : 0;
                    if (mode === 'ManualFinal' || (mode !== 'Automatic' && (parsed !== 0 || reason)) || costRaw)
                        corrections.push({ lineNo, quantity: parsed,
                            receiptUnitCost: costRaw ? Number(costRaw) : null, reason });
                }
            }
            const button = el('countReviewSubmit'); button.disabled = true;
            try {
                const count = await api('movements/counts/' + selected.reference + '/review', 'POST', {
                    clientRequestId: reviewRequestId, rowVersion: selected.rowVersion,
                    decision, varianceMode: mode, reason: el('countReviewReason').value.trim(), corrections
                });
                reviewRequestId = crypto.randomUUID(); el('countReviewReason').value = '';
                notice(decision === 'Approve' ? (bn ? 'পার্থক্য স্টক লেজারে পোস্ট হয়েছে।' : 'Variance posted to the stock ledger.') :
                    (bn ? 'গণনা বাতিল, স্টক অপরিবর্তিত।' : 'Count rejected; stock unchanged.'), true);
                await Promise.all([load(count.reference), refreshLedger()]);
            } catch (error) { notice(error.message); }
            finally { button.disabled = false; }
        });
    }
    await load();
}
