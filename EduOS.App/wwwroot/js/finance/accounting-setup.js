const root = document.getElementById('accountingSetup');
if (root) {
    const base = '/api/finance/accounting/setup', writable = root.dataset.write === 'true';
    const $ = id => document.getElementById(id);
    const safe = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    let page = 1, totalPages = 1, accounts = [], periods = [], banks = [], groups = [], assetLedgers = [];
    const alert = (message, error = false) => { const el = $('accountingNotice'); el.textContent = message; el.className = 'alert ' + (error ? 'alert-danger' : 'alert-success'); };
    const request = async (path, method = 'GET', body = null) => {
        const options = {method, credentials:'same-origin', headers:{Accept:'application/json'}};
        if (body !== null) { options.headers['Content-Type'] = 'application/json'; options.body = JSON.stringify(body); }
        if (method !== 'GET' && token) options.headers.RequestVerificationToken = token;
        const res = await fetch(base + path, options);
        let payload; try { payload = await res.json(); } catch { throw new Error('Unexpected server response.'); }
        if (!res.ok || !payload.success) throw new Error(payload.message || 'Request failed.');
        return payload.data;
    };
    const run = async fn => { try { await fn(); } catch (e) { alert(e.message, true); } };
    const types = {1:'Asset',2:'Liability',3:'Equity',4:'Income',5:'Expense'};
    const resetAccount = () => { $('accountForm')?.reset(); if (!writable) return; $('accountId').value = ''; $('accountVersion').value = ''; $('accountActive').checked = true; };
    const resetPeriod = () => { $('periodForm')?.reset(); if (!writable) return; $('periodId').value = ''; $('periodVersion').value = ''; };
    const resetBank = () => { $('bankForm')?.reset(); if (!writable) return; $('bankId').value = ''; $('bankVersion').value = ''; $('bankActive').checked = true; };
    async function loadAccounts() {
        const q = $('accountSearch').value.trim(), data = await request('/accounts?' + new URLSearchParams({page, pageSize:25, search:q}));
        accounts = data.items || []; totalPages = Math.max(1, data.totalPages || 1);
        $('accountPage').textContent = 'Page ' + page + ' of ' + totalPages + ' (' + data.totalCount + ')';
        $('accountPrev').disabled = page <= 1; $('accountNext').disabled = page >= totalPages;
        $('accountRows').innerHTML = accounts.map(a => '<tr><td>' + safe(a.code) + '</td><td>' + safe(a.name) +
            '</td><td>' + safe(types[a.accountType]) + '</td><td>' + safe(a.parentAccountName) + '</td><td>' +
            (a.isGroup ? 'Yes' : 'No') + '</td><td>' + (a.isActive ? 'Yes' : 'No') + '</td>' +
            (writable ? '<td><button class="btn btn-sm btn-outline-primary" data-edit-account="' + a.id + '">Edit</button></td>' : '') + '</tr>').join('');
    }
    async function loadAccountOptions() {
        if (!writable) return;
        let all = [], current = 1, pages = 1;
        do {
            const data = await request('/accounts?' + new URLSearchParams({page:current, pageSize:100}));
            all.push(...(data.items || [])); pages = data.totalPages || 1; current++;
            if (pages > 100) throw new Error('Too many accounts for this selector; contact administrator.');
        } while (current <= pages);
        groups = all.filter(x => x.isGroup && x.isActive);
        assetLedgers = all.filter(x => x.accountType === 1 && !x.isGroup && x.isActive);
        $('accountParent').innerHTML = '<option value="">Root</option>' + groups.map(x => '<option value="' + x.id + '">' + safe(x.code + ' — ' + x.name) + '</option>').join('');
        $('bankLedger').innerHTML = '<option value="">Select ledger</option>' + assetLedgers.map(x => '<option value="' + x.id + '">' + safe(x.code + ' — ' + x.name) + '</option>').join('');
    }
    async function loadPeriods() {
        periods = await request('/periods');
        $('periodRows').innerHTML = periods.map(p => '<tr><td>' + safe(p.name) + '</td><td>' + safe(p.startDate) +
            '</td><td>' + safe(p.endDate) + '</td><td>' + (p.isClosed ? 'Closed' : 'Open') + '</td>' +
            (writable ? '<td>' + (p.isClosed ? '' : '<button class="btn btn-sm btn-outline-primary me-2" data-edit-period="' + p.id + '">Edit</button><button class="btn btn-sm btn-outline-info me-2" data-preview-period="' + p.id + '">Review close</button><button class="btn btn-sm btn-outline-danger" data-close-period="' + p.id + '">Close</button>') + '</td>' : '') + '</tr>').join('');
    }
    async function showPeriodReadiness(id) {
        const data = await request('/periods/' + id + '/close-readiness');
        const panel = $('periodCloseReadiness');
        panel.className = 'alert ' + (data.canClose ? 'alert-success' : 'alert-warning');
        panel.textContent = data.periodName + ': ' + (data.canClose ? 'Ready to close.' : 'Not ready.') +
            ' Posted: ' + data.postedCount + ', reversed: ' + data.reversedCount +
            ', historical deleted: ' + data.historicalDeletedJournalCount +
            ', drafts: ' + data.draftCount + ', submitted: ' + data.submittedCount +
            ', approved: ' + data.approvedCount + ', invalid journals: ' + data.invalidJournalCount +
            ', invalid lines: ' + data.invalidLineCount + ', unbalanced journals: ' + data.unbalancedJournalCount +
            ', invalid accounts: ' + data.missingAccountCount + '. ' + (data.blockers || []).join(' ');
        return data;
    }
    async function loadBanks() {
        if (!writable) return;
        banks = await request('/banks');
        $('bankRows').innerHTML = banks.map(b => '<tr><td>' + safe(b.bankName) + '</td><td>' + safe(b.accountNumber) +
            '</td><td>' + safe(b.ledgerAccountName) + '</td><td>' + (b.isActive ? 'Active' : 'Inactive') +
            '</td><td><button class="btn btn-sm btn-outline-primary" data-edit-bank="' + b.id + '">Edit</button></td></tr>').join('');
    }
    $('accountSearchBtn').addEventListener('click', () => run(async () => { page = 1; await loadAccounts(); }));
    $('accountPrev').addEventListener('click', () => run(async () => { if (page > 1) { page--; await loadAccounts(); } }));
    $('accountNext').addEventListener('click', () => run(async () => { if (page < totalPages) { page++; await loadAccounts(); } }));
    if (writable) {
        $('accountReset').addEventListener('click', resetAccount);
        $('periodReset').addEventListener('click', resetPeriod);
        $('bankReset').addEventListener('click', resetBank);
        $('accountRows').addEventListener('click', e => run(async () => {
            const id = e.target.closest('[data-edit-account]')?.dataset.editAccount; if (!id) return;
            const a = await request('/accounts/' + id);
            $('accountId').value = a.id; $('accountVersion').value = a.rowVersion; $('accountCode').value = a.code;
            $('accountName').value = a.name; $('accountType').value = a.accountType;
            $('accountParent').value = a.parentAccountId || ''; $('accountGroup').checked = a.isGroup; $('accountActive').checked = a.isActive;
        }));
        $('accountForm').addEventListener('submit', e => { e.preventDefault(); run(async () => {
            const id = $('accountId').value, body = {code:$('accountCode').value.trim(), name:$('accountName').value.trim(),
                accountType:Number($('accountType').value), parentAccountId:$('accountParent').value ? Number($('accountParent').value) : null,
                isGroup:$('accountGroup').checked, isActive:$('accountActive').checked, rowVersion:$('accountVersion').value || null};
            await request('/accounts' + (id ? '/' + id : ''), id ? 'PUT' : 'POST', body);
            resetAccount(); await Promise.all([loadAccounts(), loadAccountOptions()]); alert('Account saved.');
        }); });
        $('periodRows').addEventListener('click', e => run(async () => {
            const edit = e.target.closest('[data-edit-period]')?.dataset.editPeriod;
            const close = e.target.closest('[data-close-period]')?.dataset.closePeriod;
            const preview = e.target.closest('[data-preview-period]')?.dataset.previewPeriod;
            if (preview) await showPeriodReadiness(preview);
            if (edit) {
                const p = periods.find(x => String(x.id) === edit); if (!p) return;
                $('periodId').value = p.id; $('periodVersion').value = p.rowVersion;
                $('periodName').value = p.name; $('periodFrom').value = p.startDate; $('periodTo').value = p.endDate;
            }
            if (close) {
                const p = periods.find(x => String(x.id) === close); if (!p) return;
                const readiness = await showPeriodReadiness(close);
                if (!readiness.canClose) { alert('Period is not ready to close. Resolve the displayed blockers.', true); return; }
                if (!confirm('Close period ' + p.name + '? ' + readiness.postedCount + ' posted and ' + readiness.reversedCount + ' reversed journals verified. This cannot be undone.')) return;
                await request('/periods/' + close + '/close', 'POST', {rowVersion:readiness.rowVersion});
                await loadPeriods(); alert('Period closed.');
            }
        }));
        $('periodForm').addEventListener('submit', e => { e.preventDefault(); run(async () => {
            const id = $('periodId').value, body = {name:$('periodName').value.trim(), startDate:$('periodFrom').value,
                endDate:$('periodTo').value, rowVersion:$('periodVersion').value || null};
            await request('/periods' + (id ? '/' + id : ''), id ? 'PUT' : 'POST', body);
            resetPeriod(); await loadPeriods(); alert('Period saved.');
        }); });
        $('bankRows').addEventListener('click', e => {
            const id = e.target.closest('[data-edit-bank]')?.dataset.editBank;
            const b = banks.find(x => String(x.id) === id); if (!b) return;
            $('bankId').value = b.id; $('bankVersion').value = b.rowVersion; $('bankLedger').value = b.accountId;
            $('bankName').value = b.bankName; $('bankAccountName').value = b.accountName; $('bankNumber').value = b.accountNumber;
            $('bankBranch').value = b.branchName || ''; $('bankRouting').value = b.routingNumber || ''; $('bankActive').checked = b.isActive;
        });
        $('bankForm').addEventListener('submit', e => { e.preventDefault(); run(async () => {
            const id = $('bankId').value, body = {accountId:Number($('bankLedger').value), bankName:$('bankName').value.trim(),
                accountName:$('bankAccountName').value.trim(), accountNumber:$('bankNumber').value.trim(),
                branchName:$('bankBranch').value.trim() || null, routingNumber:$('bankRouting').value.trim() || null,
                currencyCode:'BDT', isActive:$('bankActive').checked, rowVersion:$('bankVersion').value || null};
            await request('/banks' + (id ? '/' + id : ''), id ? 'PUT' : 'POST', body);
            resetBank(); await loadBanks(); alert('Bank account saved.');
        }); });
    }
    run(async () => { await Promise.all([loadAccounts(), loadPeriods()]); if (writable) await Promise.all([loadAccountOptions(), loadBanks()]); });
}
