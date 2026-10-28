(() => {
    'use strict';
    const root = document.getElementById('transcriptVerification');
    if (!root) return;
    const el = id => document.getElementById(id);
    const reference = root.dataset.reference || '';
    const labels = {
        Valid: ['Valid transcript / বৈধ ট্রান্সক্রিপ্ট', 'alert-success'],
        Revoked: ['Revoked transcript / বাতিল ট্রান্সক্রিপ্ট', 'alert-danger'],
        ReviewRequired: ['Institutional review required / প্রতিষ্ঠান কর্তৃক পুনঃযাচাই প্রয়োজন', 'alert-warning']
    };
    async function verify() {
        const status = el('transcriptVerificationStatus');
        status.className = 'alert alert-secondary';
        status.textContent = 'Checking transcript / ট্রান্সক্রিপ্ট যাচাই হচ্ছে…';
        el('transcriptVerifyAgain').disabled = true;
        try {
            const response = await fetch('/api/public/credentials/transcripts/' + encodeURIComponent(reference),
                { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
            const result = await response.json().catch(() => null);
            if (!response.ok || result?.success !== true) throw new Error('Transcript unavailable');
            const data = result.data || {};
            const label = labels[data.status] || labels.ReviewRequired;
            status.className = 'alert ' + label[1];
            status.textContent = label[0];
            el('transcriptIssuer').textContent = data.issuerName || '—';
            el('transcriptNumber').textContent = data.transcriptNumber || '—';
            el('transcriptIssueDate').textContent = data.issueDate || '—';
        } catch {
            status.className = 'alert alert-warning';
            status.textContent = 'Unable to verify / যাচাই করা যাচ্ছে না। আবার চেষ্টা করুন।';
            for (const id of ['transcriptIssuer', 'transcriptNumber', 'transcriptIssueDate'])
                el(id).textContent = '—';
        } finally {
            el('transcriptVerifyAgain').disabled = false;
        }
    }
    el('transcriptVerifyAgain').addEventListener('click', verify);
    verify();
})();
