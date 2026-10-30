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
    const add = (values) => {
        const tr = document.createElement('tr');
        values.forEach(value => {
            const td = document.createElement('td');
            td.textContent = String(value ?? '');
            tr.appendChild(td);
        });
        rows.appendChild(tr);
    };
    form.addEventListener('submit', async event => {
        event.preventDefault();
        result.hidden = true;
        rows.replaceChildren();
        const id = reference.value.trim();
        if (!guid.test(id)) {
            message.textContent = bn ? 'সঠিক UUID দিন।' : 'Enter a valid UUID.';
            return;
        }
        message.textContent = bn ? 'যাচাই হচ্ছে…' : 'Checking…';
        const button = form.querySelector('button[type="submit"]');
        button.disabled = true;
        try {
            const path = '/api/v3/enterprise/ledger-trace/' +
                (kind.value === 'asset' ? 'asset-depreciation/' : '') + encodeURIComponent(id);
            const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json' } });
            const body = await response.json();
            if (!response.ok || !body.success || !body.data)
                throw new Error(body.message || (bn ? 'ট্রেস পাওয়া যায়নি।' : 'Trace unavailable.'));
            const d = body.data;
            if (kind.value === 'asset') {
                add([bn ? 'অবচয়' : 'Depreciation', d.runReference, d.state, '', '', d.runTotalAmount]);
                add([bn ? 'বিস্তারিত' : 'Lines', d.depreciationLineCount, '', '', '', d.depreciationLinesAmount]);
            } else {
                add([bn ? 'স্টক' : 'Stock', d.movementNumber, d.state, '', '', d.recordedMovementValue]);
            }
            (d.journals || []).forEach(j => add([
                bn ? 'জার্নাল' : 'Journal', j.number, j.state, j.debit, j.credit, ''
            ]));
            status.textContent = (bn ? 'ফাইন্যান্স লিংক: ' : 'Finance evidence: ') +
                d.financeEvidenceStatus + (d.reversalReference ? ' · Reversal: ' + d.reversalReference : '');
            csv.href = path + '/csv';
            result.hidden = false;
            message.textContent = bn ? 'ট্রেস প্রস্তুত।' : 'Trace ready.';
        } catch (error) {
            message.textContent = error.message || (bn ? 'অনুরোধ ব্যর্থ।' : 'Request failed.');
        } finally {
            button.disabled = false;
        }
    });
    print.addEventListener('click', () => window.print());
    // The register reads only bounded server pages. A present journal reference
    // is not presented as balanced until the existing detail endpoint verifies it.
    // Reuse the canonical tenant-authorized campus catalogue; never ask staff
    // to guess database IDs or expose a cross-tenant campus list.
    const campusSelect = document.getElementById('v3RegisterCampus');
    fetch('/api/institution-onboarding/campus-list', {
        credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
    }).then(async response => {
        const body = await response.json();
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
    const registerPrevious = document.getElementById('v3RegisterPrevious');
    const registerNext = document.getElementById('v3RegisterNext');
    let currentPage = 1;
    let activeRequest;
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
        if (!registerForm.reportValidity()) return;
        if (activeRequest) activeRequest.abort();
        activeRequest = new AbortController();
        const request = activeRequest;
        registerResult.hidden = true;
        registerMessage.textContent = bn ? 'যাচাই হচ্ছে…' : 'Loading…';
        try {
            const q = registerQuery();
            const response = await fetch('/api/v3/enterprise/ledger-trace/register?' + q,
                { credentials: 'same-origin', cache: 'no-store', signal: request.signal,
                    headers: { Accept: 'application/json' } });
            const body = await response.json();
            if (!response.ok || !body.success || !body.data)
                throw new Error(body.message || (bn ? 'রেজিস্টার পাওয়া যায়নি।' : 'Register unavailable.'));
            if (activeRequest !== request) return;
            const data = body.data;
            registerRows.replaceChildren();
            (data.items || []).forEach(item => {
                const tr = document.createElement('tr');
                [item.date, item.number, item.state, item.recordedValue,
                    item.lineCount, item.evidenceStatus].forEach(value => {
                    const td = document.createElement('td');
                    td.textContent = String(value ?? '');
                    tr.appendChild(td);
                });
                tr.tabIndex = 0;
                tr.title = bn ? 'বিস্তারিত দেখতে Enter চাপুন' : 'Press Enter for details';
                const open = () => {
                    document.getElementById('v3TraceKind').value =
                        data.kind === 'asset' ? 'asset' : 'stock';
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
            registerCsv.href = '/api/v3/enterprise/ledger-trace/register/csv?' + q;
            registerResult.hidden = false;
            registerMessage.textContent = data.totalCount === 0
                ? (bn ? 'কোনো তথ্য নেই।' : 'No records found.') : '';
        } catch (error) {
            if (error.name !== 'AbortError')
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
