const root = document.getElementById('v3LedgerTrace');
if (root) {
    const bn = root.dataset.language === 'bn';
    const form = document.getElementById('v3TraceForm');
    const reference = document.getElementById('v3TraceReference');
    const kind = document.getElementById('v3TraceKind');
    const message = document.getElementById('v3TraceMessage');
    const result = document.getElementById('v3TraceResult');
    const rows = document.getElementById('v3TraceRows');
    const status = document.getElementById('v3TraceStatus');
    const csv = document.getElementById('v3TraceCsv');
    const print = document.getElementById('v3TracePrint');
    const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    const evidenceLabels = {
        EvidenceConflict: ['প্রমাণে অসংগতি', 'Evidence conflict'],
        JournalImbalance: ['জার্নালে ভারসাম্য নেই', 'Journal imbalance'],
        JournalAmountMismatch: ['উৎসের অঙ্ক ও জার্নালের অঙ্ক মেলে না', 'Source/journal amount mismatch'],
        UnpostedJournal: ['জার্নাল পোস্ট হয়নি', 'Unposted journal'],
        RunLineMismatch: ['রান ও লাইনের মোট মেলে না', 'Run/line mismatch'],
        NoExplicitReference: ['সরাসরি জার্নাল রেফারেন্স নেই', 'No explicit journal reference'],
        ExplicitBalancedReference: ['লিংকযুক্ত জার্নাল সমান', 'Linked journal balanced'],
        ExplicitReferencePresent: ['সরাসরি রেফারেন্স আছে', 'Explicit reference present'],
        ApprovedCommitment: ['অনুমোদিত ক্রয় প্রতিশ্রুতি যাচাইকৃত', 'Approved purchase commitment verified'],
        PendingApproval: ['ক্রয় আদেশের অনুমোদন অপেক্ষমাণ', 'Purchase order approval pending'],
        CancellationPending: ['বাতিলের অনুমোদন অপেক্ষমাণ—প্রতিশ্রুতি বহাল', 'Cancellation pending; commitment active'],
        CancelledCommitment: ['বাতিলকৃত ক্রয় প্রতিশ্রুতি', 'Cancelled purchase commitment'],
        RejectedCommitment: ['প্রত্যাখ্যাত ক্রয় আদেশ', 'Rejected purchase order'],
        DraftCommitment: ['খসড়া ক্রয় আদেশ', 'Draft purchase order'],
        VerifiedProcurementReceipt: ['ক্রয় আদেশ, পণ্য গ্রহণ ও স্টক প্রমাণ যাচাইকৃত', 'PO, goods receipt and stock evidence verified'],
        ExplicitReversedReference: ['মূল ও বিপরীত জার্নাল যাচাইকৃত', 'Original and contra journals verified'],
        ReversalPending: ['বিপরীত জার্নাল অনুমোদনের অপেক্ষায়', 'Contra journal pending approval']
    };
    const evidenceText = code => evidenceLabels[code]?.[bn ? 0 : 1] || String(code || '');
    const add = (values) => {
        const tr = document.createElement('tr');
        values.forEach(value => {
            const td = document.createElement('td');
            td.textContent = String(value ?? '');
            tr.appendChild(td);
        });
        rows.appendChild(tr);
    };
    // The server remains the authorization boundary. This shield prevents
    // previously authorized evidence from surviving a tenant/session transition.
    const session = {
        reference: document.body?.dataset.authSessionReference || '',
        tenant: document.body?.dataset.authTenantId || ''
    };
    const shield = document.getElementById('eduosSessionShield');
    let locked = false, detailGeneration = 0, detailController, activeRequest;
    const sessionCurrent = () => !locked && !!session.reference && !!session.tenant &&
        !window.EduOSSessionTransitionPending?.() &&
        document.body?.dataset.authSessionReference === session.reference &&
        document.body?.dataset.authTenantId === session.tenant;
    function lockPage() {
        if (locked) return;
        locked = true;
        detailGeneration++;
        detailController?.abort();
        activeRequest?.abort();
        for (const id of ['v3TraceRows', 'v3RegisterRows'])
            document.getElementById(id).replaceChildren();
        for (const id of ['v3TraceResult', 'v3RegisterResult'])
            document.getElementById(id).hidden = true;
        for (const id of ['v3TraceCsv', 'v3RegisterCsv']) {
            const link = document.getElementById(id);
            link.removeAttribute('href');
            link.setAttribute('aria-disabled', 'true');
            link.tabIndex = -1;
        }
        for (const id of ['v3TraceForm', 'v3RegisterForm'])
            for (const control of document.getElementById(id).querySelectorAll('input, select, button'))
                control.disabled = true;
        for (const id of ['v3RegisterPrevious', 'v3RegisterNext', 'v3TracePrint'])
            document.getElementById(id).disabled = true;
        const warning = bn ? 'প্রতিষ্ঠানের সেশন পরিবর্তিত হয়েছে। পেজটি আবার লোড করুন।' :
            'Institution session changed. Reload this page.';
        message.textContent = warning;
        document.getElementById('v3RegisterMessage').textContent = warning;
    }
    async function verifySession() {
        if (!sessionCurrent() || !shield || !window.EduOSVerifySessionContext) {
            lockPage();
            throw new Error('session-changed');
        }
        try { await window.EduOSVerifySessionContext(); }
        catch { lockPage(); throw new Error('session-changed'); }
        if (!sessionCurrent() || shield.hidden !== true) {
            lockPage();
            throw new Error('session-changed');
        }
    }
    const exportLink = link => link.addEventListener('click', async event => {
        event.preventDefault();
        const href = link.getAttribute('href');
        if (!href || !href.startsWith('/api/v3/enterprise/ledger-trace/')) return;
        try {
            await verifySession();
            if (sessionCurrent()) window.location.assign(href);
        } catch { /* The privacy shield has invalidated stale export links. */ }
    });
    exportLink(csv);
    form.addEventListener('submit', async event => {
        event.preventDefault();
        const generation = ++detailGeneration;
        detailController?.abort();
        detailController = new AbortController();
        const request = detailController;
        const button = form.querySelector('button[type="submit"]');
        button.disabled = false;
        result.hidden = true;
        rows.replaceChildren();
        csv.removeAttribute('href');
        if (!sessionCurrent()) { lockPage(); return; }
        const id = reference.value.trim();
        if (!guid.test(id)) {
            message.textContent = bn ? 'সঠিক UUID দিন।' : 'Enter a valid UUID.';
            return;
        }
        message.textContent = bn ? 'যাচাই হচ্ছে…' : 'Checking…';
        button.disabled = true;
        try {
            await verifySession();
            if (generation !== detailGeneration) return;
            const selectedKind = kind.value;
            const route = selectedKind === 'asset' ? 'asset-depreciation/' :
                selectedKind === 'procurement' ? 'procurement/' :
                selectedKind === 'supplier-payable' ? 'supplier-payable/' : '';
            const path = '/api/v3/enterprise/ledger-trace/' + route + encodeURIComponent(id);
            const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store',
                signal: request.signal, headers: { Accept: 'application/json' } });
            const body = await response.json();
            await verifySession();
            if (generation !== detailGeneration) return;
            if (response.status === 401 || response.status === 403) {
                lockPage();
                return;
            }
            if (!response.ok || !body.success || !body.data)
                throw new Error(body.message || (bn ? 'ট্রেস পাওয়া যায়নি।' : 'Trace unavailable.'));
            const d = body.data;
            if (selectedKind === 'procurement') {
                add([bn ? 'ক্রয় আদেশ' : 'Purchase order', d.orderNumber, d.state, '', '', d.totalAmount]);
                add([bn ? 'অনুমোদিত বাজেট' : 'Approved budget', d.currencyCode, '', '', '', d.approvedBudget]);
                add([bn ? 'দর-সাশ্রয়' : 'Negotiated variance', d.currencyCode, '', '', '', d.budgetVariance]);
                add([bn ? 'চাহিদাপত্র' : 'Requisition', d.requisitionReference, '', '', '', '']);
                add([bn ? 'সরবরাহকারী' : 'Supplier', d.supplierReference, '', '', '', '']);
                (d.lines || []).forEach(line => add([
                    bn ? 'ক্রয় লাইন' : 'PO line', line.lineNo + ' · Item ' + line.inventoryItemId,
                    line.quantity + ' × ' + line.unitPrice, '', '', line.amount
                ]));
            } else if (selectedKind === 'supplier-payable') {
                add([bn ? 'সরবরাহকারী বিল' : 'Supplier invoice', d.invoiceNumber, d.state, '', '', d.totalAmount]);
                add([bn ? 'স্টক মূল্য' : 'Stock value', d.purchaseOrderReference, '', '', '', d.stockValue]);
                add([bn ? 'মূল্য পার্থক্য' : 'Price variance', d.supplierReference, '', '', '', d.priceVariance]);
                (d.goodsReceiptReferences || []).forEach(r => add([bn ? 'পণ্য গ্রহণ' : 'Goods receipt', r, 'Posted', '', '', '']));
                (d.settlementInstructions || []).forEach(s => add([
                    s.isPostedToFinance ? (bn ? 'পোস্টকৃত পরিশোধ GL' : 'Posted settlement GL') :
                        (bn ? 'অপরিশোধিত নির্দেশনা' : 'UNPOSTED settlement instruction'),
                    s.reference, s.state + (s.postedReversalReference ? (bn ? ' · রিভার্সড' : ' · REVERSED') :
                        s.isPostedToFinance ? (bn ? ' · ব্যাংক নিশ্চিত' : ' · BANK CONFIRMED') :
                        (bn ? ' · অর্থ প্রদান হয়নি' : ' · NOT PAID')),
                    '', '', s.amount
                ]));
                add([bn ? 'সংরক্ষিত পরিমাণ (অপরিশোধিত)' : 'Reserved, NOT PAID',
                    d.invoiceReference, '', '', '', d.settlementReservedAmount ?? 0]);
                add([bn ? 'পোস্টকৃত নেট পরিশোধ' : 'Posted net settlement',
                    d.invoiceReference, '', '', '', d.settlementPostedNetAmount ?? 0]);
                add([bn ? 'উৎস-সংযুক্ত নেট দায়' : 'Source-linked outstanding',
                    d.invoiceReference, '', '', '', d.sourceLinkedOutstandingAmount ?? 0]);
            } else if (selectedKind === 'asset') {
                add([bn ? 'অবচয়' : 'Depreciation', d.runReference, d.state, '', '', d.runTotalAmount]);
                add([bn ? 'বিস্তারিত' : 'Lines', d.depreciationLineCount, '', '', '', d.depreciationLinesAmount]);
            } else {
                add([bn ? 'স্টক' : 'Stock', d.movementNumber, d.state, '', '', d.recordedMovementValue]);
                if (d.procurementGoodsReceiptReference) {
                    add([bn ? 'পণ্য গ্রহণ' : 'Goods receipt', d.procurementGoodsReceiptReference,
                        bn ? 'পোস্টকৃত' : 'Posted', '', '', '']);
                    add([bn ? 'ক্রয় আদেশ' : 'Purchase order', d.procurementPurchaseOrderReference,
                        bn ? 'যাচাইকৃত উৎস' : 'Verified source', '', '', '']);
                }
            }
            (d.journals || []).forEach(j => add([
                bn ? 'জার্নাল' : 'Journal', j.number, j.state, j.debit, j.credit, ''
            ]));
            status.textContent = selectedKind === 'procurement'
                ? (bn ? 'ক্রয় প্রতিশ্রুতির প্রমাণ: ' : 'Commitment evidence: ') +
                    evidenceText(d.evidenceStatus)
                : (bn ? 'ফাইন্যান্স লিংক: ' : 'Finance evidence: ') +
                    evidenceText(d.financeEvidenceStatus) +
                    (d.reversalReference ? ' · Reversal: ' + d.reversalReference : '') +
                    (d.sourceEvidenceStatus === 'VerifiedProcurementReceipt'
                        ? ' · ' + evidenceText(d.sourceEvidenceStatus) : '');
            csv.setAttribute('href', path + '/csv');
            csv.removeAttribute('aria-disabled');
            csv.tabIndex = 0;
            result.hidden = false;
            message.textContent = bn ? 'ট্রেস প্রস্তুত।' : 'Trace ready.';
        } catch (error) {
            if (generation !== detailGeneration || locked || error.name === 'AbortError') return;
            message.textContent = error.message || (bn ? 'অনুরোধ ব্যর্থ।' : 'Request failed.');
        } finally {
            if (generation === detailGeneration && !locked) {
                button.disabled = false;
                detailController = undefined;
            }
        }
    });
    print.addEventListener('click', async () => {
        if (result.hidden) return;
        try { await verifySession(); if (sessionCurrent()) window.print(); }
        catch { /* Session changed; never print stale tenant evidence. */ }
    });
    // The register reads only bounded server pages. A present journal reference
    // is not presented as balanced until the existing detail endpoint verifies it.
    // Reuse the canonical tenant-authorized campus catalogue; never ask staff
    // to guess database IDs or expose a cross-tenant campus list.
    const campusSelect = document.getElementById('v3RegisterCampus');
    fetch('/api/institution-onboarding/campus-list', {
        credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
    }).then(async response => {
        const body = await response.json();
        await verifySession();
        if (response.status === 401 || response.status === 403) {
            lockPage();
            return;
        }
        if (!response.ok || !body.success || !Array.isArray(body.data))
            throw new Error('Campus list unavailable.');
        body.data.forEach(campus => {
            const option = document.createElement('option');
            option.value = String(campus.id);
            option.textContent = campus.name + (campus.code ? ' (' + campus.code + ')' : '');
            campusSelect.appendChild(option);
        });
    }).catch(() => {
        campusSelect.title = bn ? 'ক্যাম্পাস তালিকা পাওয়া যায়নি।' : 'Campus list unavailable.';
    });
    const registerForm = document.getElementById('v3RegisterForm');
    const registerRows = document.getElementById('v3RegisterRows');
    const registerMessage = document.getElementById('v3RegisterMessage');
    const registerResult = document.getElementById('v3RegisterResult');
    const registerCount = document.getElementById('v3RegisterCount');
    const registerPage = document.getElementById('v3RegisterPage');
    const registerCsv = document.getElementById('v3RegisterCsv');
    exportLink(registerCsv);
    const registerPrevious = document.getElementById('v3RegisterPrevious');
    const registerNext = document.getElementById('v3RegisterNext');
    let currentPage = 1;
    const registerQuery = () => {
        const q = new URLSearchParams({
            kind: document.getElementById('v3RegisterKind').value,
            from: document.getElementById('v3RegisterFrom').value,
            to: document.getElementById('v3RegisterTo').value,
            page: String(currentPage),
            pageSize: document.getElementById('v3RegisterSize').value
        });
        const campus = document.getElementById('v3RegisterCampus').value.trim();
        if (campus) q.set('campusId', campus);
        return q;
    };
    const loadRegister = async () => {
        if (!sessionCurrent()) { lockPage(); return; }
        if (!registerForm.reportValidity()) return;
        if (activeRequest) activeRequest.abort();
        activeRequest = new AbortController();
        const request = activeRequest;
        registerResult.hidden = true;
        registerRows.replaceChildren();
        registerCsv.removeAttribute('href');
        registerMessage.textContent = bn ? 'যাচাই হচ্ছে…' : 'Loading…';
        try {
            await verifySession();
            if (activeRequest !== request) return;
            const q = registerQuery();
            const response = await fetch('/api/v3/enterprise/ledger-trace/register?' + q,
                { credentials: 'same-origin', cache: 'no-store', signal: request.signal,
                    headers: { Accept: 'application/json' } });
            const body = await response.json();
            await verifySession();
            if (activeRequest !== request) return;
            if (response.status === 401 || response.status === 403) {
                lockPage();
                return;
            }
            if (!response.ok || !body.success || !body.data)
                throw new Error(body.message || (bn ? 'রেজিস্টার পাওয়া যায়নি।' : 'Register unavailable.'));
            const data = body.data;
            registerRows.replaceChildren();
            (data.items || []).forEach(item => {
                const tr = document.createElement('tr');
                [item.date, item.number, item.state, item.recordedValue,
                    item.lineCount, evidenceText(item.evidenceStatus)].forEach(value => {
                    const td = document.createElement('td');
                    td.textContent = String(value ?? '');
                    tr.appendChild(td);
                });
                tr.tabIndex = 0;
                tr.title = bn ? 'বিস্তারিত দেখতে Enter চাপুন' : 'Press Enter for details';
                const open = () => {
                    if (!sessionCurrent()) { lockPage(); return; }
                    document.getElementById('v3TraceKind').value =
                        data.kind === 'asset' ? 'asset' :
                        data.kind === 'procurement' ? 'procurement' : 'stock';
                    reference.value = item.reference;
                    form.requestSubmit();
                    reference.scrollIntoView({ behavior: 'smooth', block: 'center' });
                };
                tr.addEventListener('click', open);
                tr.addEventListener('keydown', e => {
                    if (e.key === 'Enter') { e.preventDefault(); open(); }
                });
                registerRows.appendChild(tr);
            });
            registerCount.textContent = (bn ? 'মোট: ' : 'Total: ') + data.totalCount;
            registerPage.textContent = (bn ? 'পৃষ্ঠা ' : 'Page ') + data.page;
            registerPrevious.disabled = data.page <= 1;
            registerNext.disabled = data.page * data.pageSize >= data.totalCount;
            registerCsv.setAttribute('href', '/api/v3/enterprise/ledger-trace/register/csv?' + q);
            registerCsv.removeAttribute('aria-disabled');
            registerCsv.tabIndex = 0;
            registerResult.hidden = false;
            registerMessage.textContent = data.totalCount === 0
                ? (bn ? 'কোনো তথ্য নেই।' : 'No records found.') : '';
        } catch (error) {
            if (!locked && activeRequest === request && error.name !== 'AbortError')
                registerMessage.textContent = error.message ||
                    (bn ? 'অনুরোধ ব্যর্থ।' : 'Request failed.');
        } finally {
            if (activeRequest === request) activeRequest = undefined;
        }
    };
    registerForm.addEventListener('submit', e => {
        e.preventDefault(); currentPage = 1; loadRegister();
    });
    registerPrevious.addEventListener('click', () => {
        if (currentPage > 1) { currentPage--; loadRegister(); }
    });
    registerNext.addEventListener('click', () => {
        currentPage++; loadRegister();
    });

}
