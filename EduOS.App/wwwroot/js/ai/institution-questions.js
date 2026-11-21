(() => {
    'use strict';
    const root = document.getElementById('aiInstitutionWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const bn = root.dataset.lang === 'bn';
    const say = (en, bd) => bn ? bd : en;
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const session = [document.body?.dataset.authTenantId || '', document.body?.dataset.authSessionReference || ''];
    const sameSession = () => session[0] === (document.body?.dataset.authTenantId || '') &&
        session[1] === (document.body?.dataset.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    const form = el('aiInstitutionForm'), answer = el('aiInstitutionAnswer'),
        submit = el('aiInstitutionAsk'), notice = el('aiInstitutionNotice');
    let sequence = 0, abort = null;
    const val = id => (el(id)?.value || '').trim();
    const localDate = d => [d.getFullYear(), String(d.getMonth()+1).padStart(2,'0'),
        String(d.getDate()).padStart(2,'0')].join('-');
    const today = new Date(), first = new Date(today.getFullYear(),today.getMonth(),today.getDate());
    first.setDate(first.getDate() - 29);
    el('aiInstitutionTo').value = localDate(today);
    el('aiInstitutionFrom').value = localDate(first);
    function invalidate() {
        sequence++;
        abort?.abort();
        abort = null;
        submit.disabled = false;
        answer.classList.add('d-none');
        notice.textContent = '';
    }
    ['aiInstitutionQuestion','aiInstitutionFrom','aiInstitutionTo'].forEach(id =>
        el(id).addEventListener('input', invalidate));
    window.addEventListener('pagehide', invalidate);
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (!sameSession() || !form.reportValidity() || submit.disabled) return;
        const fromDate = val('aiInstitutionFrom'), toDate = val('aiInstitutionTo');
        if (fromDate > toDate) {
            notice.textContent = say('Invalid date range.', 'তারিখ সঠিক নয়।'); return;
        }
        invalidate();
        const own = ++sequence, controller = new AbortController();
        abort = controller;
        submit.disabled = true;
        notice.textContent = say('Checking institution MIS…', 'প্রতিষ্ঠানের MIS যাচাই হচ্ছে…');
        try {
            const response = await fetch('/api/ai/institution/ask', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: {Accept:'application/json','Content-Type':'application/json',
                    RequestVerificationToken:token},
                body: JSON.stringify({question:val('aiInstitutionQuestion'),fromDate,toDate}),
                signal:controller.signal
            });
            const payload = await response.json().catch(() => null);
            if (own !== sequence || !sameSession()) return;
            if (!response.ok || payload?.success !== true)
                throw Error(payload?.message || say('The metric is unavailable.', 'তথ্য পাওয়া যায়নি।'));
            const m = payload.data;
            if (typeof m?.key !== 'string' || typeof m?.value !== 'number' ||
                !['Count','BDT'].includes(m?.unit) ||
                typeof m?.definition !== 'string' || typeof m?.source !== 'string')
                throw Error('Invalid MIS response.');
            el('aiInstitutionMetric').textContent = m.key.replaceAll('_',' ');
            el('aiInstitutionValue').textContent = m.value.toLocaleString(bn ? 'bn-BD' : 'en-BD') + ' ' + m.unit;
            el('aiInstitutionDefinition').textContent = m.definition;
            el('aiInstitutionSource').textContent = m.scope + ' · ' + m.source + ' · ' +
                m.periodFrom + '–' + m.periodTo;
            answer.classList.remove('d-none');
            notice.textContent = say('Verified from the canonical institution MIS.', 'মূল MIS থেকে যাচাইকৃত।');
        } catch (e) {
            if (own === sequence && e?.name !== 'AbortError' && sameSession())
                notice.textContent = e?.message || say('Request failed.', 'অনুরোধ ব্যর্থ।');
        } finally { if (own === sequence) {submit.disabled=false; abort=null;} }
    });
})();
