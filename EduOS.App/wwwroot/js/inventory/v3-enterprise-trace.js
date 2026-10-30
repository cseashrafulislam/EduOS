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
}
