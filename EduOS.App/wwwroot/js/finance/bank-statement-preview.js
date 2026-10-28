// Read-only bank-statement comparison. Local CSV contents are sent only to the authorized
// reconciliation preview API, never stored in the browser or posted to the ledger.
const panel = document.getElementById('bankStatementPanel');
if (panel) {
    const el = id => document.getElementById(id);
    const escapeHtml = v => String(v ?? '').replace(/[&<>"']/g, c =>
        ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const decimal = /^-?\d{1,12}(?:\.\d{1,2})?$/;
    const cents = value => {
        const s = String(value ?? '').trim();
        if (!decimal.test(s)) throw new Error('Use BDT values with up to two decimal places.');
        const negative = s.startsWith('-');
        const [whole, fraction = ''] = (negative ? s.slice(1) : s).split('.');
        const n = BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
        return negative ? -n : n;
    };
    const money = value => {
        const n = cents(value), absolute = n < 0n ? -n : n;
        return (n < 0n ? '-' : '') + (absolute / 100n).toString() + '.' +
            String(absolute % 100n).padStart(2, '0');
    };
    const cell = v => {
        let text = String(v ?? '');
        if (/^[\s\u0000-\u001f]*[=+\-@]/.test(text)) text = "'" + text;
        return '"' + text.replace(/"/g, '""') + '"';
    };
    const show = (message, error = false) => {
        el('statementNotice').className = 'alert ' + (error ? 'alert-danger' : 'alert-info');
        el('statementNotice').textContent = message;
    };
    const parseCsvRow = line => {
        const fields = []; let value = '', quoted = false;
        for (let i = 0; i < line.length; i++) {
            const c = line[i];
            if (c === '"') {
                if (quoted && line[i + 1] === '"') { value += '"'; i++; }
                else if (!quoted && value.length === 0) quoted = true;
                else if (quoted) quoted = false;
                else throw new Error('Invalid CSV quoting.');
            } else if (c === ',' && !quoted) { fields.push(value); value = ''; }
            else value += c;
        }
        if (quoted) throw new Error('Multiline CSV fields are not supported.');
        fields.push(value);
        return fields.map(x => x.trim());
    };
    function parseRows(csv, from, to) {
        const lines = csv.replace(/^\uFEFF/, '').split(/\r?\n/).filter(x => x.trim());
        if (lines.length < 2 || lines.length > 501) throw new Error('CSV must contain 1–500 movements.');
        if (parseCsvRow(lines.shift()).map(x => x.toLowerCase()).join(',') !== 'date,reference,deposit,withdrawal')
            throw new Error('CSV header must be date,reference,deposit,withdrawal.');
        return lines.map((line, i) => {
            const x = parseCsvRow(line);
            if (x.length !== 4 || !/^\d{4}-\d{2}-\d{2}$/.test(x[0]) ||
                x[0] < from || x[0] > to || x[1].length > 150)
                throw new Error('Invalid CSV date, reference or column count at row ' + (i + 2) + '.');
            const deposit = cents(x[2]), withdrawal = cents(x[3]);
            if ((deposit > 0n) === (withdrawal > 0n) || deposit < 0n || withdrawal < 0n)
                throw new Error('Each row needs one positive deposit or withdrawal at row ' + (i + 2) + '.');
            return {date:x[0], reference:x[1], deposit:money(x[2]), withdrawal:money(x[3])};
        });
    }
    async function get(url, options = {}) {
        const response = await fetch(url, {credentials:'same-origin',cache:'no-store',
            headers:{Accept:'application/json',...(options.headers || {})},...options});
        const raw = await response.text();
        const safe = raw.replace(/("(?:deposit|withdrawal|openingBalance|closingBalance|ledgerOpeningBalance|ledgerClosingBalance|statementOpeningBalance|statementClosingBalance|openingVariance|movementVariance|closingVariance)"\s*:\s*)(-?\d+(?:\.\d+)?)/g, '$1"$2"');
        let payload; try { payload = JSON.parse(safe); } catch { throw new Error('Unexpected bank response.'); }
        if (!response.ok || !payload.success) throw new Error(payload.message || 'Comparison failed.');
        return payload.data;
    }
    let report = null, comparisonFrom = '', revision = 0, previewRequest = null;
    let requestKey = null, draftPage = 1, draftDetail = null, draftRevision = 0;
    let savedReview = null;
    // Any changed input invalidates the exported evidence, including edits during a request.
    const invalidate = () => {
        revision++;
        report = null;
        previewRequest = null;
        requestKey = null;
        el('statementSave').disabled = true;
        el('statementExport').disabled = true;
        el('statementSummary').textContent = '';
        el('statementMatchCount').textContent = '0';
        el('statementMatches').replaceChildren();
        el('statementExceptions').replaceChildren();
        el('statementNotice').className = 'alert d-none';
    };
    ['statementBank','statementFrom','statementTo','statementOpening','statementClosing','statementCsv']
        .forEach(id => {
            el(id).addEventListener('input', invalidate);
            el(id).addEventListener('change', invalidate);
        });
    const today = new Date().toLocaleDateString('en-CA');
    el('statementFrom').value = today.slice(0, 4) + '-01-01';
    el('statementTo').value = today;
    get('/api/finance/accounting/setup/banks').then(banks => {
        el('statementBank').innerHTML = '<option value="">Select bank</option>' +
            banks.map(x => '<option value="' + escapeHtml(x.id) + '">' +
                escapeHtml(x.bankName + ' — ' + x.accountName) + '</option>').join('');
    }).catch(e => show(e.message, true));
    el('statementFile').addEventListener('change', async event => {
        invalidate(); // Never keep an export enabled while a replacement file is being read.
        const fileRevision = revision;
        try {
            const file = event.target.files?.[0];
            if (!file) return;
            if (file.size > 80000) throw new Error('CSV must be 80 KB or smaller.');
            const content = await file.text();
            if (fileRevision !== revision) return; // Ignore a file read superseded by form edits.
            el('statementCsv').value = content;
            invalidate();
        } catch (e) { if (fileRevision === revision) show(e.message, true); }
    });
    el('bankStatementForm').addEventListener('submit', async event => {
        event.preventDefault();
        invalidate();
        const submittedRevision = revision;
        const bank = el('statementBank').value, from = el('statementFrom').value, to = el('statementTo').value;
        if (!/^\d+$/.test(bank) || BigInt(bank) <= 0n || !Number.isSafeInteger(Number(bank)) || !from || !to || from > to)
            return show('Choose a bank and valid date range.', true);
        el('statementRun').disabled = true;
        try {
            const opening = money(el('statementOpening').value), closing = money(el('statementClosing').value);
            const movements = parseRows(el('statementCsv').value, from, to);
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ||
                document.querySelector('meta[name="request-verification-token"]')?.content || '';
            const statement = {bankAccountId:Number(bank),fromDate:from,toDate:to,
                openingBalance:opening,closingBalance:closing,movements};
            const result = await get('/api/finance/accounting/reports/bank-statement-preview', {
                method:'POST', headers:{'Content-Type':'application/json',RequestVerificationToken:token},
                body:JSON.stringify(statement)
            });
            if (submittedRevision !== revision) return; // Inputs changed while comparing.
            report = result;
            previewRequest = statement;
            requestKey = crypto.randomUUID();
            el('statementSave').disabled = false;
            comparisonFrom = from;
            el('statementMatchCount').textContent = String((result.matches || []).length);
            el('statementMatches').innerHTML = (result.matches || []).map(x =>
                '<tr><td>' + escapeHtml(x.date) + '</td><td>' + escapeHtml(x.journalNumber) +
                '</td><td>' + escapeHtml(x.journalLineId) + '</td><td>' +
                escapeHtml(x.statementRowNumber) + '</td><td>' + escapeHtml(x.reference) + '</td><td class="text-end">' +
                money(x.deposit) + '</td><td class="text-end">' + money(x.withdrawal) +
                '</td></tr>').join('') ||
                '<tr><td colspan="7">No unique exact matches.</td></tr>';
            el('statementSummary').textContent = result.bankName + ': ' + result.matchedCount +
                ' matched; ' + result.referenceCollisionCount + ' reused references; ' +
                result.unmatchedLedgerCount + ' unmatched ledger; ' +
                result.unmatchedStatementCount + ' unmatched statement. Opening variance BDT ' +
                money(result.openingVariance) + '; movement variance BDT ' +
                money(result.movementVariance) + '; closing variance BDT ' +
                money(result.closingVariance) + '. ' +
                (result.isExactMatch ? 'All movements match exactly (not cleared).' :
                    'Exceptions require manual review (not cleared).');
            el('statementExceptions').innerHTML = (result.exceptions || []).map(x =>
                '<tr><td>' + escapeHtml(x.source) + '</td><td>' + escapeHtml(x.date) +
                '</td><td>' + escapeHtml(x.journalNumber) + '</td><td>' +
                escapeHtml(x.journalLineId) + '</td><td>' + escapeHtml(x.statementRowNumber) +
                '</td><td>' + escapeHtml(x.reference) + '</td><td class="text-end">' + money(x.deposit) +
                '</td><td class="text-end">' + money(x.withdrawal) +
                '</td><td>' + escapeHtml(x.reason) + '</td></tr>').join('') ||
                '<tr><td colspan="9">No unmatched movements in this exact-match preview.</td></tr>';
            el('statementExport').disabled = false;
            show('Read-only comparison complete. Differences are not approved or cleared.');
        } catch (e) { if (submittedRevision === revision) show(e.message, true); }
        finally { el('statementRun').disabled = false; }
    });
    el('statementExport').addEventListener('click', () => {
        if (!report) return;
        const rows = [['Status','Source','Date','Journal','Ledger Line ID','Line Version','Statement Row','Reference','Deposit','Withdrawal','Reason'],
            ['Summary','Bank',comparisonFrom,'','','','',report.bankName,'','','Comparison only; not approved or cleared'],
            ['Summary','Reused references','','','','','','','','',String(report.referenceCollisionCount)],
            ['Summary','Opening variance','','','','','','','','',money(report.openingVariance)],
            ['Summary','Movement variance','','','','','','','','',money(report.movementVariance)],
            ['Summary','Closing variance','','','','','','','','',money(report.closingVariance)],
            ['Summary','Exact match','','','','','','','','',report.isExactMatch ? 'Yes; not cleared' : 'No; manual review'],
            ...(report.matches || []).map(x => ['Matched','Ledger + Statement',x.date,x.journalNumber,
                x.journalLineId,x.journalLineVersion,x.statementRowNumber,x.reference,
                money(x.deposit),money(x.withdrawal),'Unique exact match; not cleared']),
            ...(report.exceptions || []).map(x => ['Unmatched',x.source,x.date,x.journalNumber,
                x.journalLineId,x.journalLineVersion,x.statementRowNumber,x.reference,
                money(x.deposit),money(x.withdrawal),x.reason])];
        const csv = '\uFEFF' + rows.map(x => x.map(cell).join(',')).join('\r\n') + '\r\n';
        const url = URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'}));
        const a = document.createElement('a'); a.href = url;
        a.download = 'bank-statement-comparison-' + comparisonFrom + '.csv';
        document.body.appendChild(a); a.click(); a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    });

    // Saving is a separate, explicit user action; comparison alone never persists data.
    const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value ||
        document.querySelector('meta[name="request-verification-token"]')?.content || '';
    const draftBase = '/api/finance/accounting/reports/bank-reconciliations';
    const download = (rows, name) => {
        const csv = '\uFEFF' + rows.map(row => row.map(cell).join(',')).join('\r\n') + '\r\n';
        const url = URL.createObjectURL(new Blob([csv], {type:'text/csv;charset=utf-8'}));
        const a = document.createElement('a'); a.href = url; a.download = name;
        document.body.appendChild(a); a.click(); a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    };
    const resetDraft = () => {
        draftRevision++;
        draftDetail = null;
        savedReview = null;
        el('statementSavedReview').classList.add('d-none');
        el('statementSavedReviewRows').replaceChildren();
        el('statementSavedReviewSummary').textContent = '';
        el('statementDraftDetail').textContent = '';
        el('statementDraftExport').classList.add('d-none');
        el('statementDraftRows').replaceChildren();
        el('statementDraftSummary').textContent = '';
        el('statementDraftPage').textContent = '';
        el('statementDraftPrev').disabled = true;
        el('statementDraftNext').disabled = true;
    };
    async function loadDrafts(page = 1) {
        resetDraft();
        const bank = el('statementBank').value, seq = draftRevision;
        if (!/^\d+$/.test(bank) || Number(bank) <= 0) return;
        try {
            const result = await get(draftBase + '?bankAccountId=' + encodeURIComponent(bank) +
                '&page=' + page + '&pageSize=25');
            if (seq !== draftRevision || el('statementBank').value !== bank) return;
            draftPage = page;
            el('statementDraftSummary').textContent = result.totalCount + ' saved drafts; no clearance or approval.';
            el('statementDraftRows').innerHTML = (result.items || []).map(x =>
                '<tr><td>' + escapeHtml(x.statementIdentifier) + '</td><td>' +
                escapeHtml(x.fromDate) + ' – ' + escapeHtml(x.toDate) + '</td><td>' +
                escapeHtml(x.movementCount) + '</td><td>Draft</td><td>' +
                escapeHtml(x.preparedAt) + '</td><td><button type="button" class="btn btn-outline-primary btn-sm" data-draft-id="' +
                escapeHtml(x.id) + '">View</button> <button type="button" class="btn btn-outline-secondary btn-sm" data-review-id="' +
                escapeHtml(x.id) + '">Review</button></td></tr>').join('') ||
                '<tr><td colspan="6">No saved statement drafts for this bank.</td></tr>';
            el('statementDraftPage').textContent = 'Page ' + page + ' of ' + Math.max(1,result.totalPages);
            el('statementDraftPrev').disabled = !result.hasPreviousPage;
            el('statementDraftNext').disabled = !result.hasNextPage;
        } catch (e) {
            if (seq === draftRevision) el('statementDraftSummary').textContent = e.message;
        }
    }
    el('statementBank').addEventListener('change', () => loadDrafts(1));
    el('statementDraftRefresh').addEventListener('click', () => loadDrafts(1));
    el('statementDraftPrev').addEventListener('click', () => loadDrafts(draftPage - 1));
    el('statementDraftNext').addEventListener('click', () => loadDrafts(draftPage + 1));
    el('statementSave').addEventListener('click', async () => {
        if (!previewRequest || !requestKey || !report) return;
        const identifier = el('statementIdentifier').value.trim();
        if (!identifier || identifier.length > 100 || /[\u0000-\u001f\u007f]/.test(identifier))
            return show('Enter a valid bank statement identifier (up to 100 characters).', true);
        const seq = revision;
        el('statementSave').disabled = true;
        try {
            const result = await get(draftBase, {
                method:'POST',
                headers:{'Content-Type':'application/json',RequestVerificationToken:token()},
                body:JSON.stringify({clientRequestId:requestKey,statementIdentifier:identifier,
                    statement:previewRequest})
            });
            if (seq !== revision) return;
            show('Saved immutable statement draft #' + result.id + '. Not approved or cleared.');
            loadDrafts(1);
        } catch (e) {
            if (seq === revision) {
                show(e.message, true);
                el('statementSave').disabled = false; // Same key on retry.
            }
        }
    });
    el('statementDraftRows').addEventListener('click', async event => {
        const button = event.target.closest('button[data-draft-id],button[data-review-id]');
        if (!button) return;
        const reviewing = button.hasAttribute('data-review-id');
        const id = reviewing ? button.dataset.reviewId : button.dataset.draftId;
        const seq = draftRevision, bank = el('statementBank').value;
        button.disabled = true;
        try {
            if (reviewing) {
                savedReview = null;
                el('statementSavedReview').classList.add('d-none');
                const result = await get(draftBase + '/' + encodeURIComponent(id) + '/review');
                if (seq !== draftRevision || el('statementBank').value !== bank) return;
                savedReview = result;
                const c = result.comparison, d = result.draft;
                el('statementSavedReviewSummary').textContent =
                    d.statementIdentifier + ' · reviewed ' + result.reviewedAtUtc +
                    ' · ' + c.matchedCount + ' unique matches, ' +
                    c.unmatchedLedgerCount + ' unmatched ledger, ' +
                    c.unmatchedStatementCount + ' unmatched statement, ' +
                    c.referenceCollisionCount + ' reused references · ' +
                    'opening variance BDT ' + money(c.openingVariance) +
                    ', closing variance BDT ' + money(c.closingVariance) +
                    ' · LIVE read-only review, not approved or cleared.';
                const matched = (c.matches || []).map(x =>
                    '<tr><td>Exact match</td><td>' + escapeHtml(x.date) +
                    '</td><td>' + escapeHtml(x.journalLineId) + '</td><td>' +
                    escapeHtml(x.statementRowNumber) + '</td><td>' + escapeHtml(x.reference) +
                    '</td><td class="text-end">' + money(x.deposit) +
                    '</td><td class="text-end">' + money(x.withdrawal) +
                    '</td><td>Review only</td></tr>');
                const exceptions = (c.exceptions || []).map(x =>
                    '<tr><td>' + escapeHtml(x.source) + '</td><td>' + escapeHtml(x.date) +
                    '</td><td>' + escapeHtml(x.journalLineId) + '</td><td>' +
                    escapeHtml(x.statementRowNumber) + '</td><td>' + escapeHtml(x.reference) +
                    '</td><td class="text-end">' + money(x.deposit) +
                    '</td><td class="text-end">' + money(x.withdrawal) +
                    '</td><td>' + escapeHtml(x.reason) + '</td></tr>');
                el('statementSavedReviewRows').innerHTML = matched.concat(exceptions).join('') ||
                    '<tr><td colspan="8">No matching or unmatched movements.</td></tr>';
                el('statementSavedReview').classList.remove('d-none');
            } else {
                const detail = await get(draftBase + '/' + encodeURIComponent(id));
                if (seq !== draftRevision || el('statementBank').value !== bank) return;
                draftDetail = detail;
                el('statementDraftDetail').textContent = 'Statement ' + detail.statementIdentifier +
                    ' · ' + detail.movementCount + ' rows · fingerprint ' + detail.sourceFingerprint +
                    ' · immutable draft, not cleared.';
                el('statementDraftExport').classList.remove('d-none');
            }
        } catch (e) {
            if (seq === draftRevision) {
                if (reviewing) show(e.message, true);
                else el('statementDraftDetail').textContent = e.message;
            }
        } finally { button.disabled = false; }
    });
    el('statementSavedReviewExport').addEventListener('click', () => {
        if (!savedReview) return;
        const d = savedReview.draft, c = savedReview.comparison;
        const rows = [
            ['Statement','Fingerprint','Reviewed UTC','Source','Date','Journal','Ledger Line','Line Version',
                'Statement Row','Reference','Deposit','Withdrawal','Review result'],
            [d.statementIdentifier,d.sourceFingerprint,savedReview.reviewedAtUtc,'Summary','','','','','','','','',
                'Read-only live review; NOT approved or cleared'],
            [d.statementIdentifier,d.sourceFingerprint,savedReview.reviewedAtUtc,'Variance','','','','','','',
                money(c.openingVariance),money(c.closingVariance),'Opening / closing variance BDT'],
            ...(c.matches || []).map(x => [d.statementIdentifier,d.sourceFingerprint,savedReview.reviewedAtUtc,
                'Exact match',x.date,x.journalNumber,x.journalLineId,x.journalLineVersion,
                x.statementRowNumber,x.reference,money(x.deposit),money(x.withdrawal),'Review only; not cleared']),
            ...(c.exceptions || []).map(x => [d.statementIdentifier,d.sourceFingerprint,savedReview.reviewedAtUtc,
                x.source,x.date,x.journalNumber,x.journalLineId,x.journalLineVersion,
                x.statementRowNumber,x.reference,money(x.deposit),money(x.withdrawal),x.reason])
        ];
        download(rows, 'bank-saved-review-' + d.id + '.csv');
    });
    el('statementDraftExport').addEventListener('click', () => {
        if (!draftDetail) return;
        download([['Statement','Fingerprint','Row','Date','Reference','Deposit','Withdrawal'],
            ...(draftDetail.movements || []).map(x => [
                draftDetail.statementIdentifier,draftDetail.sourceFingerprint,x.rowNumber,x.date,
                x.reference,money(x.deposit),money(x.withdrawal)])],
            'bank-statement-draft-' + draftDetail.id + '.csv');
    });
}
