(() => {
    'use strict';
    const root = document.getElementById('certificateVerification');
    if (!root) return;
    const el = id => document.getElementById(id);
    const reference = root.dataset.reference || '';
    const labels = {
        Valid: ['Valid certificate / বৈধ সনদ', 'alert-success'],
        Revoked: ['Revoked certificate / বাতিল সনদ', 'alert-danger'],
        ReviewRequired: ['Verification requires institutional review / প্রতিষ্ঠান কর্তৃক পুনঃযাচাই প্রয়োজন', 'alert-warning']
    };
    async function verify() {
        const status = el('certificateVerificationStatus');
        status.className = 'alert alert-secondary';
        status.textContent = 'Checking certificate / সনদ যাচাই হচ্ছে…';
        el('certificateVerifyAgain').disabled = true;
        try {
            const response = await fetch('/api/public/credentials/certificates/' + encodeURIComponent(reference),
                { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
            const result = await response.json().catch(() => null);
            if (!response.ok || result?.success !== true) throw new Error('Certificate unavailable / সনদ পাওয়া যায়নি');
            const data = result.data || {};
            const label = labels[data.status] || labels.ReviewRequired;
            status.className = 'alert ' + label[1];
            status.textContent = label[0];
            el('certificateIssuer').textContent = data.issuerName || '—';
            el('certificateNumber').textContent = data.certificateNumber || '—';
            el('certificateDate').textContent = data.issueDate || '—';
        } catch {
            status.className = 'alert alert-warning';
            status.textContent = 'Unable to verify. Please try again / যাচাই করা যাচ্ছে না। আবার চেষ্টা করুন।';
            for (const id of ['certificateIssuer', 'certificateNumber', 'certificateDate'])
                el(id).textContent = '—';
        } finally {
            el('certificateVerifyAgain').disabled = false;
        }
    }
    el('certificateVerifyAgain').addEventListener('click', verify);
    verify();
})();
