import { api as inventoryApi, el, td } from './movements-api.js';

const root = el('assetAccountingWorkspace');
if (root) {
    const maker = root.dataset.maker === 'true';
    const checker = root.dataset.checker === 'true';
    const api = (path, method = 'GET', body) => inventoryApi('asset-accounting' + path, method, body);
    const assetApi = reference => inventoryApi('assets/' + encodeURIComponent(reference) + '/capitalization-readiness');
    const dateToday = () => {
        const x = new Date(); return [x.getFullYear(), String(x.getMonth() + 1).padStart(2, '0'),
            String(x.getDate()).padStart(2, '0')].join('-');
    };
    let current = '', evidence = null, options = null, statement = null, cap = null;
    let sequence = 0, busy = false, capKey = null, eventKey = null,
        capReverseKey = null, policyKey = null;
    const capIds = ['aaCapDate', 'aaServiceDate', 'aaCapMode', 'aaCost', 'aaSalvage', 'aaLife',
        'aaPolicy', 'aaCandidate', 'aaOrdinal', 'aaCapPeriod', 'aaFixed', 'aaInventory',
        'aaAccum', 'aaDepExp', 'aaImpExp', 'aaGain', 'aaLoss', 'aaVarGain', 'aaVarLoss', 'aaCapReason'];
    const eventIds = ['aaKind', 'aaPeriod', 'aaDate', 'aaMode', 'aaAmount', 'aaClearing', 'aaReason'];
    function status(message, error = false) {
        el('aaStatus').className = 'alert alert-' + (error ? 'danger' : 'info');
        el('aaStatus').textContent = message;
    }
    // A denied, failed or superseded read must never leave previous campus financial
    // evidence or approval controls visible. Keep retry keys across uncertain writes.
    function clearEvidence(invalidate = true) {
        if (invalidate) ++sequence;
        evidence = null; options = null; statement = null; cap = null;
        el('aaResults').classList.add('d-none');
        for (const id of ['aaEvidence', 'aaCapState', 'aaBook']) el(id).textContent = '';
        for (const id of ['aaEvents', 'aaPolicyHistory', 'aaCapReview',
            'aaCandidate', 'aaCapPeriod', 'aaPeriod', 'aaPolicyPeriod',
            'aaFixed', 'aaInventory', 'aaAccum', 'aaDepExp', 'aaImpExp',
            'aaGain', 'aaLoss', 'aaVarGain', 'aaVarLoss', 'aaClearing'])
            el(id)?.replaceChildren();
    }
    function optionList(id, rows, placeholder, label) {
        const select = el(id); if (!select) return;
        select.replaceChildren(new Option(placeholder, ''));
        for (const x of rows) select.add(new Option(label(x), String(x.id)));
    }
    function accountOptions() {
        const rows = options?.accounts || [];
        for (const [id, type] of [['aaFixed', 'Asset'], ['aaInventory', 'Asset'],
            ['aaAccum', 'Asset'], ['aaDepExp', 'Expense'], ['aaImpExp', 'Expense'],
            ['aaGain', 'Income'], ['aaLoss', 'Expense'], ['aaVarGain', 'Income'],
            ['aaVarLoss', 'Expense'], ['aaClearing', 'Asset']]) {
            optionList(id, rows.filter(x => x.type === type), 'Select ' + type + ' GL',
                x => x.code + ' — ' + x.name);
        }
        for (const id of ['aaCapPeriod', 'aaPeriod', 'aaPolicyPeriod'])
            optionList(id, options?.periods || [], 'Select open period',
                x => x.name + ' (' + x.startDate + '—' + x.endDate + ')');
    }
    function chooseCap() {
        const i = Number(el('aaCandidate')?.value);
        const candidate = evidence?.candidates?.[i];
        if (candidate && el('aaCapMode')?.value === 'Automatic')
            el('aaCost').value = String(candidate.postedUnitCost || 0);
        if (candidate) el('aaOrdinal').max = String(candidate.quantity);
    }
    function renderCap() {
        if (!evidence) return;
        el('aaEvidence').textContent = evidence.assetTag + ': ' + evidence.statusCode +
            '; ' + evidence.candidateLineCount + ' verified posted receipt lines' +
            (evidence.candidateListTruncated ? ' (only first 25 shown)' : '') +
            '. An approved receipt is evidence, not itself a capitalized asset.';
        if (el('aaCandidate')) {
            optionList('aaCandidate', (evidence.candidates || []).map((x, i) => ({
                id: i, name: x.receiptReference.slice(0, 8) + ' / line ' + x.lineNo +
                    ' / ' + x.quantity + ' units @ BDT ' + x.postedUnitCost
            })), 'Choose verified receipt line', x => x.name);
        }
        chooseCap();
        el('aaCapState').textContent = cap ?
            'Capitalization: ' + cap.state + ' · BDT ' + cap.proposedCostBasis +
            (cap.financeJournalReference ? ' · Finance ' + cap.financeJournalReference : '') +
            (cap.financeReversalReference ? ' · Reversal ' + cap.financeReversalReference : '') +
            (cap.stockReversalReference ? ' · Restored stock ' + cap.stockReversalReference : '') :
            'No submitted capitalization command yet.';
        const box = el('aaCapReview'); box.replaceChildren();
        if (maker && cap?.state === 'Posted' && !cap.pendingReversalReference) {
            rowButton(box, 'Request exact GL + stock unwind', async () => {
                const period = el('aaPeriod')?.value || prompt('Open Finance accounting period ID:');
                const date = prompt('Reversal date YYYY-MM-DD:', dateToday());
                const reason = prompt('Custody-cleared correction reason (at least 10 characters):');
                if (!period || !date || !reason || reason.trim().length < 10)
                    throw new Error('Valid period, date and documented correction reason required.');
                capReverseKey ||= crypto.randomUUID();
                await api('/capitalizations/' + cap.reference + '/reversal', 'POST', {
                    clientRequestId: capReverseKey, originalRowVersion: cap.rowVersion,
                    accountingPeriodId: Number(period), reversalDate: date, reason: reason.trim()
                });
                capReverseKey = null;
            }, 'btn-outline-danger');
        }
        if (checker && cap?.pendingReversalReference && cap.pendingReversalRowVersion) {
            rowButton(box, 'Review GL + stock unwind', async () => {
                const choice = prompt('Type APPROVE or REJECT for this exact GL and unit stock correction:');
                if (choice === null) return;
                const decision = choice.trim().toUpperCase();
                if (!['APPROVE', 'REJECT'].includes(decision)) throw new Error('Type APPROVE or REJECT.');
                const reason = prompt('Independent Finance and custody review reason:');
                if (!reason || reason.trim().length < 10) throw new Error('Review reason required.');
                await api('/capitalizations/' + cap.reference + '/reversal/' +
                    cap.pendingReversalReference + '/review', 'POST', {
                        rowVersion: cap.pendingReversalRowVersion,
                        approve: decision === 'APPROVE', reason: reason.trim()
                    });
            }, 'btn-outline-danger');
        }
        if (!checker || cap?.state !== 'Submitted') return;
        for (const [decision, label] of [[true, 'Approve / Post GL & stock'], [false, 'Reject']]) {
            const button = document.createElement('button');
            button.className = 'btn btn-sm ' + (decision ? 'btn-success' : 'btn-outline-danger') + ' me-2';
            button.textContent = label;
            button.addEventListener('click', () => action(async () => {
                const reason = prompt('Independent reviewer reason (10–800 characters):');
                if (!reason || reason.trim().length < 10) throw new Error('Reason must be at least 10 characters.');
                await api('/capitalizations/' + cap.reference + '/review', 'POST',
                    { rowVersion: cap.rowVersion, approve: decision, reason: reason.trim() });
            }));
            box.append(button);
        }
    }
    function rowButton(cell, label, handler, style = 'btn-outline-primary') {
        const b = document.createElement('button'); b.type = 'button';
        b.className = 'btn btn-sm ' + style + ' me-1 mt-1';
        b.textContent = label; b.addEventListener('click', () => action(handler));
        cell.append(b);
    }
    function renderEvents() {
        const body = el('aaEvents'); body.replaceChildren();
        for (const x of statement?.events || []) {
            const tr = document.createElement('tr');
            for (const v of [x.kind + ' #' + x.id, x.effectiveDate, x.amount,
                x.state, x.journalReference || 'Not posted']) tr.append(td(v));
            const cell = document.createElement('td');
            if (checker && x.state === 'Submitted')
                rowButton(cell, 'Review / যাচাই', async () => {
                    const choice = prompt('Type APPROVE or REJECT for this ' + x.kind + ' event. Cancel leaves it unchanged.');
                    if (choice === null) return;
                    if (!['APPROVE', 'REJECT'].includes(choice.trim().toUpperCase()))
                        throw new Error('Type APPROVE or REJECT explicitly.');
                    const decision = choice.trim().toUpperCase() === 'APPROVE';
                    const reason = prompt('Reviewer reason (min 10 characters):');
                    if (!reason || reason.trim().length < 10) throw new Error('Review reason required.');
                    await api('/' + x.kind.toLowerCase() + '/' + x.id + '/review', 'POST',
                        { rowVersion: x.rowVersion, approve: decision, reason: reason.trim() });
                }, 'btn-success');
            if (maker && x.state === 'Posted' && !x.pendingReversalReference && !x.reversalJournalReference)
                rowButton(cell, 'Request exact GL reversal', async () => {
                    const period = el('aaPeriod')?.value || prompt('Open Finance accounting period ID for correction:');
                    const date = prompt('Reversal date YYYY-MM-DD:', dateToday());
                    const reason = prompt('Reversal reason (min 10 characters):');
                    if (!period || !date || !reason || reason.trim().length < 10)
                        throw new Error('Reversal period, date and reason required.');
                    await api('/' + x.kind + '/' + x.id + '/reversal', 'POST', {
                        clientRequestId: crypto.randomUUID(), originalRowVersion: x.rowVersion,
                        accountingPeriodId: Number(period), reversalDate: date, reason: reason.trim()
                    });
                }, 'btn-outline-danger');
            if (checker && x.pendingReversalReference && x.pendingReversalRowVersion)
                rowButton(cell, 'Review GL contra', async () => {
                    const choice = prompt('Type APPROVE or REJECT for this exact GL contra. Cancel leaves it unchanged.');
                    if (choice === null) return;
                    if (!['APPROVE', 'REJECT'].includes(choice.trim().toUpperCase()))
                        throw new Error('Type APPROVE or REJECT explicitly.');
                    const approve = choice.trim().toUpperCase() === 'APPROVE';
                    const reason = prompt('Independent contra review reason (min 10 characters):');
                    if (!reason || reason.trim().length < 10) throw new Error('Review reason required.');
                    await api('/' + x.kind + '/' + x.id + '/reversal/' +
                        x.pendingReversalReference + '/review', 'POST',
                        { rowVersion: x.pendingReversalRowVersion, approve, reason: reason.trim() });
                }, 'btn-outline-danger');
            tr.append(cell); body.append(tr);
        }
        if (!statement?.events?.length) {
            const tr = document.createElement('tr'), cell = td('No valuation events / কোনো পোস্টিং নেই');
            cell.colSpan = 6; tr.append(cell); body.append(tr);
        }
        el('aaBook').textContent = statement?.assetBookId
            ? 'Book #' + statement.assetBookId + ' · Cost BDT ' + statement.costBasis +
            ' · Depreciation BDT ' + statement.postedDepreciation +
            ' · Impairment BDT ' + statement.postedImpairment +
            ' · Net carrying BDT ' + statement.carryingValue +
            ' · Policy v' + statement.policyVersion + ' (' + statement.policyMode + ')' +
            ' · Remaining months ' + statement.remainingUsefulLifeMonths +
            (statement.capitalizationReversed ? ' · CAPITALIZATION REVERSED' :
                statement.disposed ? ' · DISPOSED' : ' · ACTIVE')
            : 'No approved AssetBook. Post one verified source receipt unit first.';
        const policies = el('aaPolicyHistory'); policies.replaceChildren();
        for (const p of statement?.policyHistory || []) {
            const line = document.createElement('div'); line.className = 'small mb-1';
            line.append(document.createTextNode('Policy v' + p.newPolicyVersion + ' · ' + p.state +
                ' · ' + p.effectiveFrom + ' · ' + p.mode + ' · ' +
                p.approvedRemainingMonths + ' months · BDT ' + p.newSalvageValue + ' '));
            if (checker && p.state === 'Submitted')
                rowButton(line, 'Review / যাচাই', async () => {
                    const decision = prompt('Type APPROVE or REJECT for this prospective policy:');
                    if (decision === null) return;
                    const value = decision.trim().toUpperCase();
                    if (!['APPROVE', 'REJECT'].includes(value)) throw new Error('Type APPROVE or REJECT.');
                    const reason = prompt('Independent Finance policy review reason:');
                    if (!reason || reason.trim().length < 10) throw new Error('Review reason required.');
                    await api('/depreciation-policy/' + p.reference + '/review', 'POST', {
                        rowVersion: p.rowVersion, approve: value === 'APPROVE', reason: reason.trim()
                    });
                }, 'btn-outline-primary');
            policies.append(line);
        }
        if (el('aaNewPolicyVersion')) el('aaNewPolicyVersion').value = String((statement?.policyVersion || 1) + 1);
        if (el('aaPolicySalvage')) el('aaPolicySalvage').value = String(statement?.salvageValue || 0);
    }
    async function reload() {
        const asset = current, n = ++sequence;
        clearEvidence(false);
        status('Refreshing authorized asset evidence / অনুমোদিত সম্পদের তথ্য যাচাই হচ্ছে।');
        const [nextEvidence, nextOptions, nextStatement, nextCap] = await Promise.all([
            assetApi(asset), api('/options'), api('/' + asset + '/statement'),
            api('/' + asset + '/capitalization').catch(error =>
                error.message.includes('No capitalization command') ? null : Promise.reject(error))
        ]);
        if (n !== sequence || current !== asset) return;
        evidence = nextEvidence; options = nextOptions; statement = nextStatement; cap = nextCap;
        accountOptions(); renderCap(); renderEvents();
        el('aaResults').classList.remove('d-none');
        status('Asset Finance source, ledger and open-period evidence loaded. / সম্পদ হিসাব ও প্রমাণ লোড হয়েছে।');
    }
    async function action(fn) {
        if (busy) return; busy = true;
        try { await fn(); await reload(); }
        catch (error) {
            clearEvidence();
            status(error.message, true);
        }
        finally { busy = false; }
    }
    el('aaLoad').addEventListener('click', () => action(async () => {
        const asset = el('aaAsset').value.trim();
        if (!/^[0-9a-f]{8}-[0-9a-f-]{27,36}$/i.test(asset))
            throw new Error('Valid asset GUID required.');
        clearEvidence(); current = asset;
        capKey = null; eventKey = null; capReverseKey = null; policyKey = null;
    }));
    // The load handler delegates reload through action, after current has been set.
    el('aaCapForm')?.addEventListener('submit', event => {
        event.preventDefault();
        action(async () => {
            const candidate = evidence?.candidates?.[Number(el('aaCandidate').value)];
            if (!candidate) throw new Error('Choose a verified posted receipt.');
            capKey ||= crypto.randomUUID();
            await api('/' + current + '/capitalization', 'POST', {
                clientRequestId: capKey, assetRowVersion: evidence.assetRowVersion,
                receiptReference: candidate.receiptReference, receiptLineNo: candidate.lineNo,
                receiptUnitOrdinal: Number(el('aaOrdinal').value),
                accountingPeriodId: Number(el('aaCapPeriod').value),
                capitalizationDate: el('aaCapDate').value, inServiceDate: el('aaServiceDate').value,
                policyMode: el('aaCapMode').value, proposedCostBasis: Number(el('aaCost').value),
                salvageValue: Number(el('aaSalvage').value), depreciationMethod: 'StraightLine',
                usefulLifeMonths: Number(el('aaLife').value), policyVersion: Number(el('aaPolicy').value),
                fixedAssetAccountId: Number(el('aaFixed').value),
                inventoryAccountId: Number(el('aaInventory').value),
                accumulatedDepreciationAccountId: Number(el('aaAccum').value),
                depreciationExpenseAccountId: Number(el('aaDepExp').value),
                impairmentExpenseAccountId: Number(el('aaImpExp').value),
                disposalGainAccountId: Number(el('aaGain').value),
                disposalLossAccountId: Number(el('aaLoss').value),
                varianceGainAccountId: el('aaVarGain').value ? Number(el('aaVarGain').value) : null,
                varianceLossAccountId: el('aaVarLoss').value ? Number(el('aaVarLoss').value) : null,
                reason: el('aaCapReason').value.trim()
            });
            capKey = null;
        });
    });
    el('aaEventForm')?.addEventListener('submit', event => {
        event.preventDefault(); action(async () => {
            if (!statement?.assetBookId) throw new Error('Capitalization must be posted first.');
            const kind = el('aaKind').value;
            eventKey ||= crypto.randomUUID();
            await api('/' + current + '/' + kind.toLowerCase(), 'POST', {
                clientRequestId: eventKey, accountingPeriodId: Number(el('aaPeriod').value),
                effectiveDate: el('aaDate').value, valuationMode: el('aaMode').value,
                amount: Number(el('aaAmount').value),
                proceedsClearingAccountId: el('aaClearing').value ? Number(el('aaClearing').value) : null,
                reason: el('aaReason').value.trim()
            });
            eventKey = null;
        });
    });
    el('aaPolicyForm')?.addEventListener('submit', event => {
        event.preventDefault(); action(async () => {
            if (!statement?.assetBookId || statement.disposed || statement.capitalizationReversed)
                throw new Error('An active AssetBook is required for a policy amendment.');
            policyKey ||= crypto.randomUUID();
            await api('/' + current + '/depreciation-policy', 'POST', {
                clientRequestId: policyKey, assetBookRowVersion: statement.assetBookRowVersion,
                accountingPeriodId: Number(el('aaPolicyPeriod').value),
                effectiveFrom: el('aaPolicyEffective').value,
                mode: el('aaPolicyMode').value,
                remainingUsefulLifeMonths: Number(el('aaPolicyMonths').value),
                remainingLifeCorrectionMonths: Number(el('aaPolicyCorrection').value),
                newSalvageValue: Number(el('aaPolicySalvage').value),
                newPolicyVersion: Number(el('aaNewPolicyVersion').value),
                reason: el('aaPolicyReason').value.trim()
            });
            policyKey = null;
        });
    });
    el('aaCandidate')?.addEventListener('change', chooseCap);
    for (const id of capIds) el(id)?.addEventListener('input', () => { capKey = null; });
    for (const id of eventIds) el(id)?.addEventListener('input', () => { eventKey = null; });
    for (const id of ['aaPolicyEffective', 'aaPolicyPeriod', 'aaPolicyMode',
        'aaPolicyMonths', 'aaPolicyCorrection', 'aaPolicySalvage', 'aaPolicyReason',
        'aaNewPolicyVersion']) el(id)?.addEventListener('input', () => { policyKey = null; });
    const today = dateToday();
    for (const id of ['aaCapDate', 'aaServiceDate', 'aaDate']) if (el(id)) el(id).value = today;
    if (el('aaPolicyEffective')) el('aaPolicyEffective').value = today.slice(0, 7) + '-01';
    const fromQuery = new URLSearchParams(location.search).get('asset');
    if (fromQuery) { el('aaAsset').value = fromQuery; el('aaLoad').click(); }
}
