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
    let savedReview = null, matchHistory = null, matchPage = 1, approvalStatus = null;
    let evidenceReport = null;
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
    const resetEvidence = () => {
        evidenceReport = null;
        el('statementEvidencePanel').classList.add('d-none');
        el('statementEvidenceSummary').textContent = '';
        el('statementEvidenceRows').replaceChildren();
        el('statementEvidenceApprovals').replaceChildren();
        el('statementEvidenceCsv').disabled = true;
        el('statementEvidencePrint').disabled = true;
    };
    const resetDraft = () => {
        resetEvidence();
        draftRevision++;
        draftDetail = null;
        savedReview = null;
        matchHistory = null;
        approvalStatus = null;
        el('statementApprovalSummary').textContent = '';
        el('statementApprovalRows').replaceChildren();
        el('statementApprovalAction').disabled = true;
        el('statementApprovalCsv').disabled = true;
        el('statementApprovalReason').value = '';
        el('statementMatchHistoryRows').replaceChildren();
        el('statementMatchHistorySummary').textContent = '';
        el('statementMatchHistoryPage').textContent = '';
        el('statementMatchHistoryCsv').disabled = true;
        el('statementMatchHistoryPrev').disabled = true;
        el('statementMatchHistoryNext').disabled = true;
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
                escapeHtml(x.movementCount) + '</td><td>' + escapeHtml(({1:'Draft',2:'Submitted',3:'Cleared',4:'Reversal pending',5:'Reversed'})[x.stateCode] || 'Unknown') + '</td><td>' +
                escapeHtml(x.preparedAt) + '</td><td><button type="button" class="btn btn-outline-primary btn-sm" data-draft-id="' +
                escapeHtml(x.id) + '">View</button> <button type="button" class="btn btn-outline-secondary btn-sm" data-review-id="' +
                escapeHtml(x.id) + '">Review</button> <button type="button" class="btn btn-outline-secondary btn-sm" data-evidence-id="' +
                escapeHtml(x.id) + '">Evidence</button></td></tr>').join('') ||
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
    const matchBase = draftBase + '/match-events';
    async function loadMatchHistory(page = 1) {
        if (!savedReview) return;
        const seq = draftRevision, batchId = savedReview.draft.id, bank = el('statementBank').value;
        const data = await get(draftBase + '/' + encodeURIComponent(batchId) +
            '/match-events?page=' + page + '&pageSize=50');
        if (seq !== draftRevision || !savedReview || savedReview.draft.id !== batchId ||
            el('statementBank').value !== bank) return;
        matchHistory = data;
        matchPage = page;
        const rows = data.items || [];
        el('statementMatchHistorySummary').textContent = data.totalCount +
            ' recorded match decisions. Clearance requires independent approval.';
        el('statementMatchHistoryRows').innerHTML = rows.map(x =>
            '<tr><td>' + (x.actionCode === 1 ? (x.isWithdrawn ? 'Withdrawn match' : 'Confirmed match') : 'Withdrawal event') +
            '</td><td>' + escapeHtml(x.statementRowNumber) + '</td><td>' +
            escapeHtml(x.journalLineId) + '</td><td>' + escapeHtml(x.date) +
            '</td><td class="text-end">' + money(x.deposit) + ' / ' + money(x.withdrawal) +
            '</td><td>' + escapeHtml(x.actorUserId) + '</td><td>' + escapeHtml(x.eventAt) +
            '</td><td>' + (x.actionCode === 1 && !x.isWithdrawn && (!approvalStatus || approvalStatus.stateCode === 1)
                ? '<button type="button" class="btn btn-outline-danger btn-sm" data-withdraw-match="' +
                    escapeHtml(x.id) + '">Withdraw</button>' : '') + '</td></tr>').join('') ||
            '<tr><td colspan="8">No draft match decisions.</td></tr>';
        el('statementMatchHistoryPage').textContent = 'Page ' + page + ' of ' + Math.max(1, data.totalPages);
        el('statementMatchHistoryPrev').disabled = !data.hasPreviousPage;
        el('statementMatchHistoryNext').disabled = !data.hasNextPage;
        el('statementMatchHistoryCsv').disabled = !rows.length;
    }
    async function recordMatch(command, button) {
        if (!savedReview || button.disabled) return;
        const seq = draftRevision, batch = savedReview.draft, bank = el('statementBank').value;
        button.disabled = true;
        try {
            const result = await get(matchBase, {
                method:'POST', headers:{'Content-Type':'application/json',RequestVerificationToken:token()},
                body:JSON.stringify({clientRequestId:button.dataset.requestKey ||
                    (button.dataset.requestKey = crypto.randomUUID()),
                    batchId:batch.id,batchRowVersion:batch.rowVersion,...command})
            });
            if (seq !== draftRevision || !savedReview || savedReview.draft.id !== batch.id ||
                el('statementBank').value !== bank) return;
            show('Draft matching event #' + result.id + ' recorded. Not approved or cleared.');
            resetEvidence();
            await loadMatchHistory(1);
        } catch (e) { if (seq === draftRevision) show(e.message, true); }
        finally { button.disabled = false; }
    }
    el('statementSavedReviewRows').addEventListener('click', event => {
        const button = event.target.closest('button[data-confirm-row]');
        if (!button || !savedReview) return;
        recordMatch({actionCode:1,statementRowNumber:Number(button.dataset.confirmRow),
            journalLineId:Number(button.dataset.confirmLine),
            journalLineVersion:button.dataset.confirmVersion},button);
    });
    const approvalLabels = {1:'Draft',2:'Submitted for independent review',3:'Cleared — immutable evidence',
        4:'Reversal awaiting independent review',5:'Clearance reversed'};
    const approvalActions = {1:'Submit for Review',2:'Approve & Clear',3:'Request Reversal',4:'Approve Reversal'};
    async function loadApprovalStatus() {
        if (!savedReview) return;
        const seq = draftRevision, id = savedReview.draft.id, bank = el('statementBank').value;
        const result = await get(draftBase + '/' + encodeURIComponent(id) + '/approval');
        if (seq !== draftRevision || !savedReview || savedReview.draft.id !== id ||
            el('statementBank').value !== bank) return;
        approvalStatus = result;
        el('statementApprovalSummary').textContent = approvalLabels[result.stateCode] || 'Unknown state';
        el('statementApprovalRows').innerHTML = (result.events || []).map(x =>
            '<tr><td>' + escapeHtml(approvalLabels[x.actionCode]) + '</td><td>' +
            escapeHtml(x.actorUserId) + '</td><td>' + escapeHtml(x.eventAt) + '</td><td>' +
            escapeHtml(x.matchedCount) + '</td><td><code>' + escapeHtml(x.evidenceFingerprint) +
            '</code></td><td>' + escapeHtml(x.reason) + '</td></tr>').join('') ||
            '<tr><td colspan="6">No approval transitions recorded.</td></tr>';
        const button = el('statementApprovalAction');
        button.disabled = !approvalActions[result.stateCode];
        button.textContent = approvalActions[result.stateCode] || 'Workflow complete';
        button.dataset.requestKey = '';
        el('statementApprovalCsv').disabled = !(result.events || []).length;
    }
    el('statementApprovalAction').addEventListener('click', async event => {
        const button = event.currentTarget;
        if (!savedReview || !approvalStatus || button.disabled) return;
        const state = approvalStatus.stateCode;
        if (!approvalActions[state]) return;
        const reason = el('statementApprovalReason').value.trim();
        if (state >= 3 && (!reason || reason.length > 250))
            return show('Enter a reversal reason (up to 250 characters).', true);
        if (!window.confirm(approvalActions[state] + '? This creates permanent audit evidence.')) return;
        const seq = draftRevision, id = savedReview.draft.id, bank = el('statementBank').value;
        button.disabled = true;
        try {
            const result = await get(draftBase + '/approval', {
                method:'POST', headers:{'Content-Type':'application/json',RequestVerificationToken:token()},
                body:JSON.stringify({clientRequestId:button.dataset.requestKey ||
                    (button.dataset.requestKey = crypto.randomUUID()),batchId:id,
                    batchRowVersion:savedReview.draft.rowVersion,actionCode:state + 1,
                    reason:state >= 3 ? reason : null})
            });
            if (seq !== draftRevision || !savedReview || savedReview.draft.id !== id ||
                el('statementBank').value !== bank) return;
            show('Reconciliation transition #' + result.id + ' recorded; posted journals unchanged.');
            resetEvidence();
            await loadApprovalStatus();
            await loadMatchHistory(1);
        } catch (e) { if (seq === draftRevision) show(e.message, true); }
        finally { if (seq === draftRevision && approvalStatus?.stateCode === state) button.disabled = false; }
    });
    el('statementApprovalCsv').addEventListener('click', () => {
        if (!savedReview || !approvalStatus) return;
        download([['Statement','Action','Actor','Recorded UTC','Matched','Evidence SHA256','Reason'],
            ...(approvalStatus.events || []).map(x => [savedReview.draft.statementIdentifier,
                approvalLabels[x.actionCode],x.actorUserId,x.eventAt,x.matchedCount,
                x.evidenceFingerprint,x.reason])],
            'bank-clearance-history-' + savedReview.draft.id + '.csv');
    });
    el('statementMatchHistoryRows').addEventListener('click', event => {
        const button = event.target.closest('button[data-withdraw-match]');
        if (!button || !savedReview) return;
        if (!window.confirm('Withdraw this pending match? The decision will remain in audit history.')) return;
        recordMatch({actionCode:2,originalMatchEventId:Number(button.dataset.withdrawMatch),
            reason:'Withdrawn after manual review'},button);
    });
    el('statementMatchHistoryPrev').addEventListener('click', () =>
        loadMatchHistory(matchPage - 1).catch(e => show(e.message, true)));
    el('statementMatchHistoryNext').addEventListener('click', () =>
        loadMatchHistory(matchPage + 1).catch(e => show(e.message, true)));
    el('statementMatchHistoryCsv').addEventListener('click', () => {
        if (!savedReview || !matchHistory) return;
        download([['Event ID','Statement','Action','Statement row','Journal line','Line version',
            'Date','Reference','Deposit','Withdrawal','Actor','Recorded UTC','Withdrawn','Reason'],
            ...(matchHistory.items || []).map(x => [x.id,savedReview.draft.statementIdentifier,
                x.actionCode === 1 ? 'Confirm' : 'Withdraw',x.statementRowNumber,x.journalLineId,
                x.journalLineVersion,x.date,x.reference,money(x.deposit),money(x.withdrawal),
                x.actorUserId,x.eventAt,x.isWithdrawn ? 'Yes' : 'No',x.reason])],
            'bank-match-history-' + savedReview.draft.id + '-page-' + matchPage + '.csv');
    });
    // Historical report: snapshot and approval evidence only; never claim current live bank clearance.
    el('statementDraftRows').addEventListener('click', async event => {
        const button = event.target.closest('button[data-evidence-id]');
        if (!button) return;
        const id = button.dataset.evidenceId, seq = draftRevision, bank = el('statementBank').value;
        resetEvidence();
        button.disabled = true;
        try {
            const result = await get(draftBase + '/' + encodeURIComponent(id) + '/evidence');
            if (seq !== draftRevision || el('statementBank').value !== bank ||
                String(result.statement.bankAccountId) !== bank) return;
            evidenceReport = result;
            const d = result.statement;
            const label = approvalLabels[result.stateCode] || 'Unknown';
            el('statementEvidenceSummary').textContent =
                d.statementIdentifier + ' · ' + d.fromDate + ' – ' + d.toDate +
                ' · ' + label + ' · ' + result.confirmedCount + ' confirmed, ' +
                result.unmatchedCount + ' unmatched · deposits BDT ' +
                money(result.totalDeposits) + ', withdrawals BDT ' +
                money(result.totalWithdrawals) + '. Historical evidence; not a live bank balance.';
            el('statementEvidenceRows').innerHTML = (result.rows || []).map(x =>
                '<tr><td>' + escapeHtml(x.rowNumber) + '</td><td>' + escapeHtml(x.date) +
                '</td><td>' + escapeHtml(x.reference) + '</td><td class="text-end">' +
                money(x.deposit) + '</td><td class="text-end">' + money(x.withdrawal) +
                '</td><td>' + escapeHtml(x.matchEventId || 'Unmatched') +
                '</td><td>' + escapeHtml(x.journalLineId) + '</td><td>' +
                escapeHtml(x.journalLineVersion) + '</td></tr>').join('');
            el('statementEvidenceApprovals').innerHTML = (result.approvalEvents || []).map(x =>
                '<tr><td>' + escapeHtml(approvalLabels[x.actionCode]) + '</td><td>' +
                escapeHtml(x.actorUserId) + '</td><td>' + escapeHtml(x.eventAt) +
                '</td><td>' + escapeHtml(x.matchedCount) + '</td><td><code>' +
                escapeHtml(x.evidenceFingerprint) + '</code></td><td>' +
                escapeHtml(x.reason) + '</td></tr>').join('') ||
                '<tr><td colspan="6">No approval history.</td></tr>';
            el('statementEvidencePanel').classList.remove('d-none');
            el('statementEvidenceCsv').disabled = false;
            el('statementEvidencePrint').disabled = false;
        } catch (e) { if (seq === draftRevision) show(e.message, true); }
        finally { button.disabled = false; }
    });
    const evidenceRows = () => {
        const r = evidenceReport, d = r.statement;
        return [
            ['Statement','State','Period From','Period To','Opening','Closing','Fingerprint'],
            [d.statementIdentifier,approvalLabels[r.stateCode],d.fromDate,d.toDate,
                money(d.openingBalance),money(d.closingBalance),d.sourceFingerprint],
            ['Row','Date','Reference','Deposit','Withdrawal','Match Event','Journal Line','Archived Line Version'],
            ...(r.rows || []).map(x => [x.rowNumber,x.date,x.reference,money(x.deposit),
                money(x.withdrawal),x.matchEventId || 'Unmatched',x.journalLineId,x.journalLineVersion]),
            ['Total','','',money(r.totalDeposits),money(r.totalWithdrawals),
                r.confirmedCount + ' confirmed',r.unmatchedCount + ' unmatched',''],
            ['Action','Actor','UTC','Matched','Evidence SHA256','Reason'],
            ...(r.approvalEvents || []).map(x => [approvalLabels[x.actionCode],x.actorUserId,
                x.eventAt,x.matchedCount,x.evidenceFingerprint,x.reason])
        ];
    };
    el('statementEvidenceCsv').addEventListener('click', () => {
        if (!evidenceReport) return;
        download(evidenceRows(), 'bank-reconciliation-evidence-' + evidenceReport.statement.id + '.csv');
    });
    el('statementEvidencePrint').addEventListener('click', () => {
        if (!evidenceReport) return;
        const popup = window.open('', '_blank', 'width=980,height=740');
        if (!popup) return show('Allow a print window to print historical evidence.', true);
        popup.opener = null;
        const doc = popup.document;
        doc.title = 'Bank reconciliation evidence ' + evidenceReport.statement.id;
        const heading = doc.createElement('h1');
        heading.textContent = 'Bank reconciliation — historical evidence';
        doc.body.appendChild(heading);
        const note = doc.createElement('p');
        note.textContent = el('statementEvidenceSummary').textContent;
        doc.body.appendChild(note);
        const rows = evidenceRows();
        const table = doc.createElement('table');
        table.setAttribute('border', '1');
        table.setAttribute('cellpadding', '5');
        rows.forEach(values => {
            const tr = doc.createElement('tr');
            values.forEach(value => {
                const td = doc.createElement('td');
                td.textContent = String(value ?? '');
                tr.appendChild(td);
            });
            table.appendChild(tr);
        });
        doc.body.appendChild(table);
        popup.focus();
        popup.print();
    });
    el('statementDraftRows').addEventListener('click', async event => {
        const button = event.target.closest('button[data-draft-id],button[data-review-id]');
        if (!button) return;
        const reviewing = button.hasAttribute('data-review-id');
        const id = reviewing ? button.dataset.reviewId : button.dataset.draftId;
        resetEvidence();
        const seq = draftRevision, bank = el('statementBank').value;
        button.disabled = true;
        try {
            if (reviewing) {
                savedReview = null;
                matchHistory = null;
                approvalStatus = null;
                el('statementMatchHistoryCsv').disabled = true;
                el('statementMatchHistoryRows').replaceChildren();
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
                    ' · LIVE ledger comparison; clearance state is shown below.';
                const matched = (c.matches || []).map(x =>
                    '<tr><td>Exact match</td><td>' + escapeHtml(x.date) +
                    '</td><td>' + escapeHtml(x.journalLineId) + '</td><td>' +
                    escapeHtml(x.statementRowNumber) + '</td><td>' + escapeHtml(x.reference) +
                    '</td><td class="text-end">' + money(x.deposit) +
                    '</td><td class="text-end">' + money(x.withdrawal) +
                    '</td><td><button type="button" class="btn btn-outline-primary btn-sm" ' +
                    'data-confirm-row="' + escapeHtml(x.statementRowNumber) +
                    '" data-confirm-line="' + escapeHtml(x.journalLineId) +
                    '" data-confirm-version="' + escapeHtml(x.journalLineVersion) +
                    '">Confirm Draft Match</button></td></tr>');
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
                await loadApprovalStatus();
                await loadMatchHistory(1);
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
                x.statementRowNumber,x.reference,money(x.deposit),money(x.withdrawal),'Current live review; see separate clearance evidence']),
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
