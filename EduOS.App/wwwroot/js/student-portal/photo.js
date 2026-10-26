(() => {
    'use strict';
    const form = document.getElementById('portalPhotoForm');
    if (!form) return;
    const preview = document.getElementById('portalPhotoPreview');
    const input = document.getElementById('portalPhotoFile');
    const submit = document.getElementById('portalPhotoSubmit');
    const status = document.getElementById('portalPhotoStatus');
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    let student = '', rowVersion = '', requestId = null, generation = 0;
    const validPath = path => typeof path === 'string' &&
        /^\/api\/portal\/students\/[a-f0-9-]{36}\/photo\/[a-f0-9-]{36}$/i.test(path) &&
        path.toLowerCase().startsWith('/api/portal/students/' + student.toLowerCase() + '/photo/');
    function show(path) {
        preview.removeAttribute('src');
        preview.classList.add('d-none');
        if (!validPath(path)) return;
        preview.src = path;
        preview.classList.remove('d-none');
    }
    preview.addEventListener('error', () => preview.classList.add('d-none'));
    async function load(reference) {
        const current = ++generation;
        student = reference || '';
        rowVersion = '';
        requestId = null;
        input.value = '';
        submit.disabled = true;
        status.textContent = '';
        show('');
        if (!student) return;
        try {
            const response = await fetch('/api/portal/students/' + encodeURIComponent(student) + '/photo', {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data)
                throw new Error(payload?.message || 'Student photo unavailable.');
            if (current !== generation) return;
            rowVersion = payload.data.rowVersion || '';
            show(payload.data.photoUrl);
            submit.disabled = !rowVersion;
        } catch (error) {
            if (current === generation) status.textContent = error.message;
        }
    }
    document.addEventListener('portal:student-selected', event => load(event.detail?.student));
    input.addEventListener('change', () => { requestId = null; status.textContent = ''; });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        const selected = input.files?.[0];
        if (!student || !rowVersion || !selected || submit.disabled) return;
        if (!/\.(jpe?g|png|webp)$/i.test(selected.name) || selected.size < 12 ||
            selected.size > 2097152 || !['image/jpeg','image/png','image/webp'].includes(selected.type)) {
            status.textContent = 'Choose a valid JPG, PNG or WebP photo up to 2 MB.'; return;
        }
        if (!token) { status.textContent = 'Security token missing. Reload the portal.'; return; }
        if (!requestId) requestId = crypto.randomUUID();
        const current = generation, reference = student;
        const data = new FormData();
        data.append('file', selected);
        data.append('requestId', requestId);
        data.append('rowVersion', rowVersion);
        submit.disabled = true;
        status.textContent = 'Scanning and saving photo…';
        try {
            const response = await fetch('/api/portal/students/' + encodeURIComponent(reference) + '/photo', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json', RequestVerificationToken: token }, body: data
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data)
                throw new Error(payload?.message || 'Photo upload failed. Retry the same file.');
            if (current !== generation) return;
            rowVersion = payload.data.rowVersion;
            show(payload.data.photoUrl);
            input.value = '';
            requestId = null;
            status.textContent = 'Profile photo saved.';
        } catch (error) {
            if (current === generation) status.textContent = error.message;
        } finally {
            if (current === generation) submit.disabled = !rowVersion;
        }
    });
})();