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

    function showStatus(message, failed = false) {
        status.textContent = message;
        status.className = failed ? 'small mt-2 text-danger' : 'small mt-2 text-muted';
    }
    function syncButton() {
        const chosen = Number(select.value);
        submit.disabled = loading || !Number.isSafeInteger(chosen) ||
            !available.has(chosen) || chosen === currentTenantId;
    }
    modal.addEventListener('show.bs.modal', async () => {
        loading = true;
        currentTenantId = null;
        available = new Set();
        select.replaceChildren();
        select.disabled = true;
        syncButton();
        showStatus(document.documentElement.lang === 'bn-BD' ? 'লোড হচ্ছে…' : 'Loading…');
        try {
            const response = await fetch('/api/auth/institutions', {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
            });
            if (!response.ok) throw new Error('Unable to load institutions.');
            const payload = await response.json();
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
                (document.documentElement.lang === 'bn-BD' ? 'অন্য কোনো সক্রিয় প্রতিষ্ঠান নেই।' : 'No other active institution.') : '');
        } catch (error) {
            showStatus(error.message || 'Unable to load institutions.', true);
        } finally {
            loading = false;
            syncButton();
        }
    });
    select.addEventListener('change', syncButton);
    submit.addEventListener('click', async () => {
        const tenantId = Number(select.value);
        if (loading || !Number.isSafeInteger(tenantId) || !available.has(tenantId) ||
            tenantId === currentTenantId) return;
        loading = true;
        syncButton();
        showStatus(document.documentElement.lang === 'bn-BD' ? 'পরিবর্তন হচ্ছে…' : 'Switching…');
        try {
            const response = await fetch('/api/auth/institutions/switch', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                body: JSON.stringify({ tenantId })
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
        }
    });
})();
