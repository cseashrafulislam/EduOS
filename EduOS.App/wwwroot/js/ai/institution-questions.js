(() => {
    'use strict';
    const root = document.getElementById('aiInstitutionWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const bn = root.dataset.lang === 'bn';
    const say = (en, bd) => bn ? bd : en;
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const session = [document.body?.dataset.authTenantId || '',
        document.body?.dataset.authSessionReference || ''];
    const sameSession = () => session[0] === (document.body?.dataset.authTenantId || '') &&
        session[1] === (document.body?.dataset.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    const form = el('aiInstitutionForm'), answer = el('aiInstitutionAnswer'),
        submit = el('aiInstitutionAsk'), notice = el('aiInstitutionNotice'),
        consent = el('aiInstitutionConsent');
    let sequence = 0, abort = null, generalAiAvailable = false;
    const val = id => (el(id)?.value || '').trim();
    const localDate = d => [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'),
        String(d.getDate()).padStart(2, '0')].join('-');
    const today = new Date();
    const first = new Date(today.getFullYear(), today.getMonth(), today.getDate());
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
    el('aiInstitutionQuestion').addEventListener('input', () => {
        consent.checked = false;
        invalidate();
    });
    ['aiInstitutionFrom', 'aiInstitutionTo'].forEach(id =>
        el(id).addEventListener('input', invalidate));
    window.addEventListener('pagehide', invalidate);

    async function post(url, payload, signal) {
        const response = await fetch(url, {
            method: 'POST', credentials: 'same-origin', cache: 'no-store',
            headers: {Accept: 'application/json', 'Content-Type': 'application/json',
                RequestVerificationToken:token},
            body: JSON.stringify(payload), signal
        });
        const result = await response.json().catch(() => null);
        return {
            ok: response.ok && result?.success === true,
            status: response.status,
            message: result?.message || '',
            data: result?.data
        };
    }

    function showVerified(m) {
        if (typeof m?.key !== 'string' || typeof m?.value !== 'string' ||
            !/^-?\d+(?:\.\d+)?$/.test(m.value) ||
            !['Count', 'BDT'].includes(m?.unit) ||
            typeof m?.definition !== 'string' || typeof m?.source !== 'string')
            throw Error('Invalid MIS response.');
        el('aiInstitutionMetric').textContent = m.key.replaceAll('_', ' ');
        // Money remains invariant decimal text; never pass through JS Number.
        el('aiInstitutionValue').textContent = m.value + ' ' + m.unit;
        el('aiInstitutionValue').classList.remove('d-none');
        el('aiInstitutionDefinition').textContent = m.definition;
        el('aiInstitutionSource').textContent = m.scope + ' · ' + m.source + ' · ' +
            m.periodFrom + '–' + m.periodTo;
        answer.classList.remove('d-none');
        notice.textContent = say('Verified from institution records. No AI key or external call used.',
            'প্রতিষ্ঠানের ডাটাবেস থেকে যাচাইকৃত। API কী বা বাহ্যিক AI ব্যবহার হয়নি।');
    }

    function showGenerated(d) {
        if (typeof d?.text !== 'string' || !d.text.trim() || d.text.length > 12000 ||
            d.isOfficial !== false || typeof d.notice !== 'string')
            throw Error('Invalid external AI response.');
        el('aiInstitutionMetric').textContent = say('General explanation — AI draft',
            'সাধারণ ব্যাখ্যা — AI খসড়া');
        el('aiInstitutionValue').textContent = '';
        el('aiInstitutionValue').classList.add('d-none');
        el('aiInstitutionDefinition').textContent = d.text;
        el('aiInstitutionSource').textContent = d.notice;
        answer.classList.remove('d-none');
        notice.textContent = say('External AI generated an answer. Verify before use.',
            'বাহ্যিক AI উত্তর তৈরি করেছে। ব্যবহারের আগে যাচাই করুন।');
    }

    function localUnsupported(result) {
        // Only an explicitly unsupported local question may be routed to the
        // optional external provider. Never bypass canonical MIS 403/409/errors.
        return result.status === 400 &&
            result.message.startsWith('Unsupported question.');
    }

    async function loadStatus() {
        try {
            const response = await fetch('/api/ai/institution/status', {
                method: 'GET', cache: 'no-store', credentials: 'same-origin',
                headers: {Accept: 'application/json'}
            });
            const body = await response.json().catch(() => null);
            if (!response.ok || body?.success !== true || !sameSession()) return;
            generalAiAvailable = body.data?.generalAiAvailable === true;
        } catch {
            generalAiAvailable = false;
        } finally {
            if (sameSession())
                el('aiInstitutionMode').textContent = generalAiAvailable
                    ? say('Verified local Q&A is free. General AI is optional and requires consent.',
                        'যাচাইকৃত Local Q&A বিনামূল্যে। সাধারণ AI প্রশ্নে সম্মতি প্রয়োজন।')
                    : say('Verified local Q&A works without a key. General AI is not configured.',
                        'API কী ছাড়াই Local Q&A চালু। সাধারণ AI এখন কনফিগার করা নেই।');
        }
    }
    void loadStatus();

    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (!sameSession() || !form.reportValidity() || submit.disabled) return;
        const fromDate = val('aiInstitutionFrom'), toDate = val('aiInstitutionTo');
        if (fromDate > toDate) {
            notice.textContent = say('Invalid date range.', 'তারিখ সঠিক নয়।');
            return;
        }
        invalidate();
        const own = ++sequence, controller = new AbortController();
        abort = controller;
        submit.disabled = true;
        const question = val('aiInstitutionQuestion'), agreed = consent.checked;
        notice.textContent = say('Checking verified institution metrics…',
            'প্রতিষ্ঠানের যাচাইকৃত তথ্য খোঁজা হচ্ছে…');
        try {
            // Local MIS is always the first source, even when an AI key exists.
            const local = await post('/api/ai/institution/ask',
                {question, fromDate, toDate}, controller.signal);
            if (own !== sequence || !sameSession()) return;
            if (local.ok) {
                showVerified(local.data);
                return;
            }
            if (!localUnsupported(local))
                throw Error(local.message || say('Institution metric unavailable.',
                    'প্রতিষ্ঠানের তথ্য পাওয়া যায়নি।'));
            if (!generalAiAvailable)
                throw Error(say('No verified match. Choose a suggested question. General AI needs an institution API key.',
                    'যাচাইকৃত প্রশ্নের মিল নেই। সাজেস্ট করা প্রশ্ন বেছে নিন। সাধারণ AI-এর জন্য প্রতিষ্ঠানের API কী প্রয়োজন।'));
            if (!agreed)
                throw Error(say('No verified match. For a general question, check external AI consent and ask again.',
                    'যাচাইকৃত প্রশ্নের মিল নেই। সাধারণ জ্ঞানের প্রশ্ন হলে বাহ্যিক AI সম্মতি দিয়ে আবার জিজ্ঞাসা করুন।'));

            // Same Q&A page, separate rate-limited external endpoint. The
            // server independently rejects school-record and sensitive prompts.
            notice.textContent = say('Preparing a general AI explanation…',
                'সাধারণ AI ব্যাখ্যা তৈরি হচ্ছে…');
            const generated = await post('/api/ai/institution/general',
                {question, consentToExternalAi:agreed}, controller.signal);
            if (own !== sequence || !sameSession()) return;
            if (!generated.ok)
                throw Error(generated.message || say('General AI unavailable.',
                    'সাধারণ AI পাওয়া যাচ্ছে না।'));
            showGenerated(generated.data);
        } catch (error) {
            if (own === sequence && error?.name !== 'AbortError' && sameSession())
                notice.textContent = error?.message || say('Request failed.', 'অনুরোধ ব্যর্থ।');
        } finally {
            if (own === sequence) {
                submit.disabled = false;
                abort = null;
                // Each external request requires a fresh intentional consent.
                consent.checked = false;
            }
        }
    });
})();
