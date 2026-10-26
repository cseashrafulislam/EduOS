export function setupRevokedGuardianPaging({ $, api, state, renderRevokedGuardianLinks, notice, render }) {
    const previous = $('guardianRevokedPrev');
    const next = $('guardianRevokedNext');
    const indicator = $('guardianRevokedPage');
    const box = $('guardianRevokedLinks');
    const pageSize = 20;
    let page = 1;
    let total = 0;
    let requestSequence = 0;
    let activeReference = '';

    function updateControls() {
        const pages = Math.max(1, Math.ceil(total / pageSize));
        previous.disabled = page <= 1 || state.busy;
        next.disabled = page >= pages || state.busy;
        indicator.textContent = total ? 'Page ' + page + ' of ' + pages + ' · ' + total + ' revoked' : 'No revoked relationships';
    }

    async function refresh(reference, keepPage = false) {
        if (!reference) return;
        if (!keepPage || activeReference !== reference) page = 1;
        activeReference = reference;
        const sequence = ++requestSequence;
        previous.disabled = true;
        next.disabled = true;
        box.textContent = 'Loading revoked relationships…';
        try {
            const data = await api('/api/student-guardians/students/' +
                encodeURIComponent(reference) + '/revoked?page=' + page + '&pageSize=' + pageSize);
            if (sequence !== requestSequence || state.student?.studentReference !== reference) return;
            total = Math.max(0, Number(data.totalCount) || 0);
            // A concurrent reactivation may empty the last page; return to the last valid page.
            const lastPage = Math.max(1, Math.ceil(total / pageSize));
            if (page > lastPage) {
                page = lastPage;
                await refresh(reference, true);
                return;
            }
            state.student.revokedGuardians = Array.isArray(data.items) ? data.items : [];
            renderRevokedGuardianLinks({ $, api, state, render, notice });
            updateControls();
        } catch (error) {
            if (sequence !== requestSequence || state.student?.studentReference !== reference) return;
            box.textContent = 'Unable to load revoked relationships.';
            indicator.textContent = '';
            notice(error.message, true);
        }
    }

    previous.addEventListener('click', () => {
        if (state.busy || page <= 1) return;
        page--;
        refresh(activeReference, true);
    });
    next.addEventListener('click', () => {
        if (state.busy || page * pageSize >= total) return;
        page++;
        refresh(activeReference, true);
    });
    return { refresh };
}
