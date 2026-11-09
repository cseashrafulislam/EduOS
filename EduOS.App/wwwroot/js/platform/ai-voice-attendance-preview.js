/* Opt-in, same-origin reviewed attendance intent preview. The transcript is sent
 * only after the user clicks Review question; no canonical report is executed,
 * no provider is contacted and no business entity is changed. */
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root && root.document) api.mount(root.document, root);
})(typeof window !== 'undefined' ? window : null, function () {
    'use strict';

    const campusEndpoint = '/api/ai/voice/campuses';
    const previewEndpoint = '/api/ai/voice/attendance/preview';

    async function responseJson(response) {
        let data;
        try { data = await response.json(); } catch { data = null; }
        if (!response.ok) {
            const error = new Error(response.status === 401 || response.status === 403
                ? 'Access denied. Your current role or module does not allow attendance previews.'
                : (data && typeof data.message === 'string' && response.status < 500
                    ? data.message : 'The preview service is unavailable. Try again later.'));
            error.status = response.status;
            throw error;
        }
        if (!data || typeof data !== 'object') throw new Error('Invalid preview response.');
        return data;
    }

    async function findCampuses(fetcher, search, signal) {
        if (typeof fetcher !== 'function' || String(search || '').length > 80)
            throw new Error('Invalid campus search.');
        const term = String(search || '').trim();
        const url = campusEndpoint + (term ? '?search=' + encodeURIComponent(term) : '');
        const response = await fetcher(url, {
            method: 'GET', credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json' }, signal
        });
        const data = await responseJson(response);
        if (!Array.isArray(data.campuses) || data.campuses.length > 50)
            throw new Error('Invalid campus list.');
        return data;
    }

    async function previewAttendance(fetcher, question, campusId, csrf, signal) {
        const prompt = String(question || '').trim();
        const id = Number(campusId);
        if (typeof fetcher !== 'function' || prompt.length < 2 || prompt.length > 512 ||
            !Number.isSafeInteger(id) || id < 1 || !String(csrf || '').trim())
            throw new Error('Enter a question, select a campus and reload if your security token is missing.');
        const response = await fetcher(previewEndpoint, {
            method: 'POST', credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', 'Content-Type': 'application/json',
                RequestVerificationToken: csrf },
            body: JSON.stringify({ question: prompt, campusId: id }), signal
        });
        return responseJson(response);
    }

    function mount(doc, host) {
        const dialog = doc.getElementById('pageAssistantDialog');
        const panel = doc.getElementById('pageAssistantAiPanel');
        const question = doc.getElementById('pageAssistantQuery');
        const campusSearch = doc.getElementById('pageAssistantCampusSearch');
        const find = doc.getElementById('pageAssistantCampusFind');
        const select = doc.getElementById('pageAssistantCampusSelect');
        const preview = doc.getElementById('pageAssistantAiPreview');
        const result = doc.getElementById('pageAssistantAiResult');
        if (!dialog || !panel || !question || !campusSearch || !find || !select || !preview || !result) return;

        const bn = (doc.documentElement.lang || '').toLowerCase().startsWith('bn');
        const t = (english, bangla) => bn ? bangla : english;
        const tenant = doc.body?.dataset.authTenantId || '';
        const session = doc.body?.dataset.authSessionReference || '';
        const currentContext = () => dialog.open &&
            (doc.body?.dataset.authTenantId || '') === tenant &&
            (doc.body?.dataset.authSessionReference || '') === session;
        const csrf = () => doc.querySelector('meta[name="request-verification-token"]')?.content || '';
        let requestSequence = 0;
        let pendingRequest = null;
        function cancel() {
            requestSequence++;
            if (pendingRequest) pendingRequest.abort();
            pendingRequest = null;
            find.disabled = false;
        }
        function display(value) {
            result.replaceChildren();
            const item = doc.createElement('p');
            item.textContent = value;
            result.append(item);
        }
        function requestStarted() {
            cancel();
            const sequence = requestSequence;
            const controller = typeof host.AbortController === 'function' ? new host.AbortController() : null;
            pendingRequest = controller;
            return { sequence, signal: controller?.signal };
        }
        function stillValid(request) { return currentContext() && request.sequence === requestSequence; }
        function showFailure(error, request) {
            if (stillValid(request) && error?.name !== 'AbortError')
                display(error?.status === 401 || error?.status === 403
                    ? t('Attendance preview is not allowed for this account.', 'এই অ্যাকাউন্টে হাজিরা প্রশ্ন যাচাইয়ের অনুমতি নেই।')
                    : (error?.message || t('Unable to load. Try again.', 'লোড করা যায়নি। আবার চেষ্টা করুন।')));
        }
        async function loadCampuses() {
            if (!currentContext() || !panel.open) return;
            const request = requestStarted();
            find.disabled = true;
            preview.disabled = true;
            select.disabled = true;
            select.replaceChildren();
            display(t('Loading authorized campuses…', 'অনুমোদিত ক্যাম্পাস লোড হচ্ছে…'));
            try {
                const data = await findCampuses(host.fetch?.bind(host), campusSearch.value, request.signal);
                if (!stillValid(request)) return;
                for (const campus of data.campuses) {
                    const id = Number(campus?.id);
                    if (!Number.isSafeInteger(id) || id <= 0 || typeof campus.name !== 'string') continue;
                    const option = doc.createElement('option');
                    option.value = String(id);
                    option.textContent = campus.name + (campus.code ? ` (${campus.code})` : '');
                    select.append(option);
                }
                select.disabled = select.children.length === 0;
                preview.disabled = select.disabled;
                display(!select.disabled
                    ? (data.hasMore
                        ? t('More campuses exist. Narrow the name and search again.', 'আরও ক্যাম্পাস আছে। নাম দিয়ে আবার খুঁজুন।')
                        : t('Choose a campus, then review the question.', 'ক্যাম্পাস বেছে প্রশ্ন যাচাই করুন।'))
                    : t('No authorized campus matched.', 'অনুমোদিত ক্যাম্পাস পাওয়া যায়নি।'));
            } catch (error) { showFailure(error, request); }
            finally { if (stillValid(request)) { find.disabled = false; pendingRequest = null; } }
        }
        async function review() {
            if (!currentContext() || !panel.open || select.disabled || !select.value) return;
            const prompt = question.value.trim();
            if (prompt.length < 2 || prompt.length > 512) {
                display(t('Enter a short attendance question first.', 'আগে হাজিরা-সংক্রান্ত ছোট প্রশ্ন লিখুন।'));
                return;
            }
            const campusId = select.value;
            const request = requestStarted();
            preview.disabled = true;
            find.disabled = true;
            display(t('Checking the reviewed read-only grammar…', 'শুধু পড়ার প্রশ্নটি যাচাই হচ্ছে…'));
            try {
                const data = await previewAttendance(host.fetch?.bind(host), prompt, campusId, csrf(), request.signal);
                if (!stillValid(request) || question.value.trim() !== prompt || select.value !== campusId) return;
                if (data.decision !== 'Ready') {
                    display(typeof data.message === 'string' ? data.message :
                        t('Clarify the status and date in your question.', 'প্রশ্নে অবস্থা ও তারিখ স্পষ্ট করুন।'));
                    return;
                }
                if (data.reportExecuted !== false || !data.campus || typeof data.date !== 'string' ||
                    !['Present', 'Absent'].includes(data.attendanceStatus)) {
                    display(t('The returned preview was invalid.', 'প্রাপ্ত প্রিভিউ সঠিক নয়।'));
                    return;
                }
                display(t(`Reviewed: ${data.campus.name} · ${data.date} · ${data.attendanceStatus}. No count was run. Select a batch/session in Attendance Reports.`,
                    `যাচাই হয়েছে: ${data.campus.name} · ${data.date} · ${data.attendanceStatus}। কোনো গণনা হয়নি। Attendance Reports-এ ব্যাচ/সেশন বেছে নিন।`));
                const link = doc.createElement('a');
                link.href = '/Attendance/Reports';
                link.className = 'btn btn-outline-primary btn-sm';
                link.textContent = t('Open Attendance Reports', 'হাজিরা রিপোর্ট খুলুন');
                result.append(link);
            } catch (error) { showFailure(error, request); }
            finally {
                if (stillValid(request)) { preview.disabled = select.disabled; find.disabled = false; pendingRequest = null; }
            }
        }

        find.addEventListener('click', loadCampuses);
        preview.addEventListener('click', review);
        campusSearch.addEventListener('keydown', e => {
            if (e.key === 'Enter') { e.preventDefault(); loadCampuses(); }
        });
        question.addEventListener('input', () => { cancel(); result.replaceChildren(); preview.disabled = select.disabled; });
        select.addEventListener('change', () => { cancel(); result.replaceChildren(); preview.disabled = select.disabled; });
        panel.addEventListener('toggle', () => {
            if (panel.open && currentContext() && select.children.length === 1 && !select.value) loadCampuses();
            if (!panel.open) { cancel(); result.replaceChildren(); }
        });
        dialog.addEventListener('close', () => { cancel(); result.replaceChildren(); });
        host.addEventListener?.('pagehide', cancel);
    }

    return { findCampuses, previewAttendance, mount };
});
