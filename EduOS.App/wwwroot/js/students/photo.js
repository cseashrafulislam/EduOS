(() => {
    'use strict';
    const form = document.getElementById('studentPhotoForm');
    if (!form) return;
    const file = document.getElementById('studentPhotoFile');
    const button = document.getElementById('studentPhotoSubmit');
    const status = document.getElementById('studentPhotoStatus');
    const preview = document.getElementById('studentPhotoPreview');
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    let student = '', rowVersion = '', requestId = null, generation = 0;
    const validPath = path => typeof path === 'string' &&
        /^\/api\/students\/[a-f0-9-]{36}\/photo\/[a-f0-9-]{36}$/i.test(path) &&
        path.toLowerCase().startsWith('/api/students/' + student.toLowerCase() + '/photo/');
    function showPhoto(path) {
        preview.removeAttribute('src');
        preview.classList.add('d-none');
        if (!validPath(path)) return;
        preview.src = path;
        preview.classList.remove('d-none');
    }
    preview.addEventListener('error', () => preview.classList.add('d-none'));
    document.addEventListener('student:opened', event => {
        generation++;
        student = event.detail?.reference || '';
        rowVersion = event.detail?.rowVersion || '';
        requestId = null;
        file.value = '';
        status.textContent = '';
        button.disabled = !student || !rowVersion;
        showPhoto(event.detail?.photoUrl);
    });
    file.addEventListener('change', () => { requestId = null; status.textContent = ''; });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        const selected = file.files?.[0];
        if (!student || !rowVersion || !selected) return;
        if (!/\.(jpe?g|png|webp)$/i.test(selected.name) || selected.size < 12 ||
            selected.size > 2097152 || !['image/jpeg', 'image/png', 'image/webp'].includes(selected.type)) {
            status.textContent = 'Choose a valid JPG, PNG or WebP image up to 2 MB.'; return;
        }
        if (!token) { status.textContent = 'Security token missing. Reload the page.'; return; }
        if (!requestId) requestId = crypto.randomUUID();
        const ref = student, current = generation;
        const body = new FormData();
        body.append('file', selected);
        body.append('requestId', requestId);
        body.append('rowVersion', rowVersion);
        button.disabled = true;
        status.textContent = 'Scanning and saving photo…';
        try {
            const response = await fetch('/api/students/' + encodeURIComponent(ref) + '/photo', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json', RequestVerificationToken: token }, body
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data)
                throw new Error(payload?.message || 'Photo could not be saved.');
            if (current !== generation) return;
            rowVersion = payload.data.rowVersion;
            showPhoto(payload.data.photoUrl);
            file.value = '';
            requestId = null;
            status.textContent = 'Profile photo saved.';
        } catch (error) {
            if (current === generation) status.textContent = error.message;
        } finally {
            if (current === generation) button.disabled = false;
        }
    });
})();
