import { api, alert } from './workspace-core.js?v=1';

const base = messageId => '/api/communication/messages/' + encodeURIComponent(String(messageId)) + '/attachments';
const maxBytes = 5 * 1024 * 1024;

export async function showMessageAttachments(messageId, host) {
    const data = await api(base(messageId));
    host.replaceChildren();
    const list = document.createElement('div');
    list.className = 'small mt-2';
    for (const file of data?.items || []) {
        const link = document.createElement('a');
        link.className = 'd-block mb-1';
        link.href = base(messageId) + '/' + encodeURIComponent(file.reference) + '/download';
        link.textContent = (file.fileName || 'Attachment') + ' (' + Math.ceil(Number(file.sizeBytes) / 1024) + ' KB)';
        list.append(link);
    }
    if (!data?.items?.length) {
        const empty = document.createElement('span');
        empty.textContent = 'No attachments.';
        list.append(empty);
    }
    host.append(list);
    if (!data?.canAttach || (data.items || []).length >= 3) return;
    const form = document.createElement('form');
    form.className = 'd-flex flex-wrap align-items-center gap-2 mt-2';
    const input = document.createElement('input');
    input.type = 'file'; input.className = 'form-control form-control-sm';
    input.style.maxWidth = '300px';
    input.accept = '.pdf,.png,.jpg,.jpeg,.webp';
    input.setAttribute('aria-label', 'Choose a message attachment');
    const submit = document.createElement('button');
    submit.type = 'submit'; submit.className = 'btn btn-outline-primary btn-sm';
    submit.textContent = 'Attach file';
    const note = document.createElement('small');
    note.className = 'text-muted'; note.textContent = 'PDF or image, up to 5 MB; maximum three files.';
    form.append(input, submit, note); host.append(form);
    let pending = null, fingerprint = null, uploading = false;
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (uploading) return;
        const file = input.files?.[0];
        if (!file || file.size < 1 || file.size > maxBytes) {
            alert('Choose a file between 1 byte and 5 MB.'); return;
        }
        const csrf = document.querySelector('meta[name="request-verification-token"]')?.content;
        if (!csrf) { alert('Security token missing. Reload the page.'); return; }
        const signature = [file.name, file.size, file.lastModified, file.type].join('|');
        if (fingerprint !== signature) { fingerprint = signature; pending = crypto.randomUUID(); }
        const body = new FormData();
        body.append('requestId', pending);
        body.append('file', file);
        uploading = true; submit.disabled = true;
        try {
            const response = await fetch(base(messageId), {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json', RequestVerificationToken: csrf }, body
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success)
                throw new Error(result?.message || 'Attachment upload failed.');
            pending = null; fingerprint = null;
            await showMessageAttachments(messageId, host);
            alert('Attachment uploaded.', true);
        } catch (error) {
            alert('Attachment not confirmed; retry the same file to reuse its request ID. ' + error.message);
        } finally {
            uploading = false; submit.disabled = false;
        }
    });
}
