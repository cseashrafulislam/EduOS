(() => {
    'use strict';
    if (!document.getElementById('studentPortalTitle')) return;
    const extra = document.getElementById('studentPortalExtra');
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    let generation = 0;
    document.addEventListener('portal:documents-loaded', async event => {
        const student = event.detail?.student;
        if (!student || !extra) return;
        const current = ++generation;
        if (document.querySelector('#studentPortalTabs button.btn-primary')?.dataset.portalTab !== 'documents') return;
        const form = document.createElement('form');
        form.className = 'border rounded p-3 mt-3';
        const heading = document.createElement('h4');
        heading.className = 'h6'; heading.textContent = 'Upload a student document';
        const help = document.createElement('p');
        help.className = 'small text-muted';
        help.textContent = 'PDF, JPG, PNG, WEBP, DOC or DOCX, maximum 5 MB. Sensitive documents require institutional verification.';
        const type = document.createElement('select');
        type.className = 'form-select mb-2'; type.required = true; type.setAttribute('aria-label', 'Document type');
        type.add(new Option('Loading permitted document types…', ''));
        const title = document.createElement('input');
        title.className = 'form-control mb-2'; title.placeholder = 'Document title (optional)';
        title.maxLength = 250; title.setAttribute('aria-label', 'Document title');
        const file = document.createElement('input');
        file.type = 'file'; file.required = true; file.className = 'form-control mb-2';
        file.accept = '.pdf,.jpg,.jpeg,.png,.webp,.doc,.docx';
        file.setAttribute('aria-label', 'Choose document');
        const submit = document.createElement('button');
        submit.type = 'submit'; submit.className = 'btn btn-primary btn-sm';
        submit.textContent = 'Upload document'; submit.disabled = true;
        const status = document.createElement('div');
        status.className = 'small mt-2'; status.setAttribute('role', 'status');
        form.append(heading, help, type, title, file, submit, status);
        extra.append(form);
        let requestId = null;
        [type, title, file].forEach(input => input.addEventListener('change', () => { requestId = null; }));
        try {
            const response = await fetch('/api/portal/document-types', {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success) throw new Error(payload?.message || 'Document types unavailable.');
            if (current !== generation || !form.isConnected) return;
            type.replaceChildren(new Option('Choose document type', ''));
            for (const item of payload.data || []) type.add(new Option(item.name, item.code));
            submit.disabled = !(payload.data || []).length;
            if (submit.disabled) status.textContent = 'No permitted document types are configured.';
        } catch (error) { if (form.isConnected) status.textContent = error.message; }
        form.addEventListener('submit', async e => {
            e.preventDefault();
            if (submit.disabled || !file.files?.length) return;
            const selected = file.files[0];
            if (selected.size < 1 || selected.size > 5242880) {
                status.textContent = 'Choose a file up to 5 MB.'; return;
            }
            if (!token) { status.textContent = 'Security token missing. Reload the portal.'; return; }
            if (!requestId) requestId = crypto.randomUUID();
            const data = new FormData();
            data.append('file', selected);
            data.append('requestId', requestId);
            data.append('documentTypeCode', type.value);
            data.append('title', title.value.trim());
            submit.disabled = true; status.textContent = 'Scanning and uploading…';
            try {
                const response = await fetch('/api/portal/students/' + encodeURIComponent(student) + '/documents', {
                    method: 'POST', credentials: 'same-origin', cache: 'no-store',
                    headers: { Accept: 'application/json', RequestVerificationToken: token }, body: data
                });
                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload?.success)
                    throw new Error(payload?.message || 'Upload failed. Retry with the same file.');
                requestId = null;
                status.textContent = 'Document uploaded successfully.';
                document.getElementById('studentPortalRefresh')?.click();
            } catch (error) {
                status.textContent = error.message;
                submit.disabled = false;
            }
        });
    });
})();
