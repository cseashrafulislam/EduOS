(() => {
    'use strict';
    const modal = document.getElementById('institutionSwitchModal');
    if (!modal) return;
    const select = document.getElementById('institutionSwitchSelect');
    const submit = document.getElementById('institutionSwitchSubmit');
    const status = document.getElementById('institutionSwitchStatus');
    let currentTenantId = null;
    let available = new Set();
    let loading = false;
    let listRequestId = 0;
    let listController = null;
    const bangla = document.documentElement.lang === 'bn-BD';

    function isCurrentReadback(response) {
        const reference = document.body?.dataset.authSessionReference;
        const tenantId = document.body?.dataset.authTenantId ?? '';
        return /^[0-9A-F]{64}$/.test(reference || '') &&
            response.headers?.get('X-EduOS-Session-Reference') === reference &&
            response.headers?.get('X-EduOS-Tenant-Id') === tenantId &&
            window.EduOSSessionTransitionPending?.() !== true;
    }

    async function reconcileSession() {
        if (typeof window.EduOSVerifySessionContext === 'function')
            await window.EduOSVerifySessionContext();
        else window.location.replace('/Dashboard/Index');
    }

    function showStatus(message, failed = false) {
        status.textContent = message;
        status.className = failed ? 'small mt-2 text-danger' : 'small mt-2 text-muted';
    }
    function syncButton() {
        const chosen = Number(select.value);
        submit.disabled = loading || window.EduOSSessionTransitionPending?.() === true ||
            !Number.isSafeInteger(chosen) ||
            !available.has(chosen) || chosen === currentTenantId;
    }
    modal.addEventListener('show.bs.modal', async () => {
        const requestId = ++listRequestId;
        listController?.abort();
        const controller = new AbortController();
        listController = controller;
        const timeout = window.setTimeout(() => controller.abort(), 10000);
        let sessionMismatch = false;
        loading = true;
        currentTenantId = null;
        available = new Set();
        select.replaceChildren();
        select.disabled = true;
        syncButton();
        showStatus(bangla ? 'লোড হচ্ছে…' : 'Loading…');
        try {
            const response = await fetch('/api/auth/institutions', {
                credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json' }, signal: controller.signal
            });
            if (requestId !== listRequestId) return;
            if (response.status === 401 || response.status === 403) {
                window.location.assign('/Account/Login');
                return;
            }
            if (!response.ok) throw new Error('Unable to load institutions.');
            const payload = await response.json();
            if (requestId !== listRequestId) return;
            // A delayed response from a previous cookie/tenant must never render
            // institution names or enable a switch under a newer browser session.
            if (!isCurrentReadback(response)) {
                sessionMismatch = true;
                throw new Error('Account session changed. Reload to continue.');
            }
            if (payload.success !== true || !Array.isArray(payload.data?.institutions))
                throw new Error('Institution list is unavailable.');
            const id = Number(payload.data.currentTenantId);
            currentTenantId = Number.isSafeInteger(id) && id > 0 ? id : null;
            for (const institution of payload.data.institutions) {
                const value = Number(institution.id);
                if (!Number.isSafeInteger(value) || value <= 0 || available.has(value)) continue;
                available.add(value);
                const option = document.createElement('option');
                option.value = String(value);
                option.textContent = String(institution.name || 'Institution');
                select.append(option);
            }
            if (currentTenantId !== null && available.has(currentTenantId))
                select.value = String(currentTenantId);
            select.disabled = available.size < 2;
            showStatus(available.size < 2 ?
                (bangla ? 'অন্য কোনো সক্রিয় প্রতিষ্ঠান নেই।' : 'No other active institution.') : '');
        } catch (error) {
            if (requestId !== listRequestId) return;
            showStatus(error.message || 'Unable to load institutions.', true);
            if (sessionMismatch) await reconcileSession();
        } finally {
            window.clearTimeout(timeout);
            if (requestId === listRequestId) {
                listController = null;
                loading = false;
                syncButton();
            }
        }
    });
    modal.addEventListener('hidden.bs.modal', () => {
        // Closing or reopening the dialog invalidates any delayed list response.
        ++listRequestId;
        listController?.abort();
        listController = null;
        loading = false;
        currentTenantId = null;
        available.clear();
        select.replaceChildren();
        select.disabled = true;
        syncButton();
    });
    select.addEventListener('change', syncButton);
    submit.addEventListener('click', async () => {
        const tenantId = Number(select.value);
        if (loading || window.EduOSSessionTransitionPending?.() === true ||
            !Number.isSafeInteger(tenantId) || !available.has(tenantId) ||
            tenantId === currentTenantId) return;
        loading = true;
        syncButton();
        showStatus(bangla ? 'পরিবর্তন হচ্ছে…' : 'Switching…');
        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), 10000);
        try {
            const response = await fetch('/api/auth/institutions/switch', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                body: JSON.stringify({ tenantId }), signal: controller.signal
            });
            const payload = await response.json().catch(() => null);
            // A concurrent switch or revoked cookie must not retry with stale context.
            if (response.status === 401 || response.status === 409) {
                window.location.assign('/Account/Login');
                return;
            }
            if (!response.ok || payload?.success !== true || payload.data?.tenantId !== tenantId)
                throw new Error(payload?.message || 'Institution switch failed.');
            // Only accept the known same-origin post-switch destination.
            const redirect = payload.data.redirectUrl;
            if (redirect !== '/Dashboard/Index')
                throw new Error('Invalid institution redirect.');
            navigator.serviceWorker?.controller?.postMessage({ type: 'EDUOS_CLEAR_RUNTIME_STATIC' });
            window.EduOSNotifySessionChanged?.();
            window.location.replace(redirect);
        } catch (error) {
            showStatus(error.message || 'Institution switch failed.', true);
            // The server may already have committed the tenant switch even if
            // the response was lost or malformed. Shield this old page and
            // reconcile the cookie before allowing another attempt.
            try {
                if (typeof window.EduOSVerifySessionContext === 'function')
                    await window.EduOSVerifySessionContext();
                else window.location.replace('/Dashboard/Index');
            } catch {
                // Never leave an old tenant page exposed if verification itself fails.
                window.location.replace('/Dashboard/Index');
            }
            loading = false;
            syncButton();
        } finally {
            window.clearTimeout(timeout);
        }
    });
})();
