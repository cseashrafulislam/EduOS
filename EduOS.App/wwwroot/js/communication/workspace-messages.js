import { el, api, alert, action, dateText } from './workspace-core.js?v=1';
import { showMessageAttachments } from './workspace-message-attachments.js?v=1';

let threadPage = 1, threadPages = 1, threadVersion = 0, searchVersion = 0, messageVersion = 0;
let active = null, recipient = null, messagePage = 1, sending = false, pendingId = null, pendingPayload = null;

function button(label, click) {
    const b = document.createElement('button');
    b.type = 'button'; b.className = 'list-group-item list-group-item-action text-start';
    b.textContent = label; b.addEventListener('click', click); return b;
}
function composeState() {
    el('cwSendMessage').disabled = !recipient || recipient.id <= 0 || sending;
    el('cwMessageSubject').disabled = !!active;
}
function selectRecipient(id, name) {
    recipient = { id: Number(id), name: String(name) }; active = null; messageVersion++;
    messagePage = 1; pendingId = null; pendingPayload = null;
    el('cwConversationTitle').textContent = 'New message to ' + recipient.name;
    el('cwConversationMessages').replaceChildren();
    el('cwOlderMessages').classList.add('d-none');
    el('cwMessageSubject').value = ''; el('cwMessageBody').value = '';
    el('cwRecipientResults').replaceChildren(); composeState();
}
export async function loadThreads() {
    const version = ++threadVersion;
    const data = await api('/api/communication/threads?' + new URLSearchParams({ page: String(threadPage), pageSize: '15' }));
    if (version !== threadVersion) return;
    threadPages = Math.max(1, Number(data?.totalPages) || 1);
    el('cwThreadPage').textContent = 'Page ' + threadPage + ' of ' + threadPages;
    el('cwThreadPrev').disabled = threadPage <= 1; el('cwThreadNext').disabled = threadPage >= threadPages;
    const list = el('cwThreadRows'); list.replaceChildren();
    for (const row of data?.items || []) {
        const label = (row.peerName || 'Member') + (row.subject ? ' — ' + row.subject : '') +
            (row.unreadCount ? ' (' + row.unreadCount + ' unread)' : '') + ' · ' + dateText(row.lastMessageAt);
        list.append(button(label, () => openThread(row).catch(e => alert(e.message))));
    }
    if (!data?.items?.length) list.append(button('No conversations yet.', () => {}));
}
async function searchRecipients() {
    const term = el('cwRecipientQuery').value.trim(), version = ++searchVersion;
    if (term.length < 2 || term.length > 80) { alert('Enter 2 to 80 characters to search.'); return; }
    const rows = await api('/api/communication/message-recipients?' + new URLSearchParams({ query: term }));
    if (version !== searchVersion) return;
    const list = el('cwRecipientResults'); list.replaceChildren();
    for (const row of rows || []) list.append(button(row.name, () => selectRecipient(row.userId, row.name)));
    if (!rows?.length) list.append(button('No active tenant members found.', () => {}));
}
async function loadMessages(older = false) {
    if (!active) return;
    const reference = active.reference, version = ++messageVersion;
    const data = await api('/api/communication/threads/' + encodeURIComponent(reference) + '/messages?' +
        new URLSearchParams({ page: String(messagePage), pageSize: '20' }));
    if (version !== messageVersion || active?.reference !== reference) return;
    const box = el('cwConversationMessages');
    if (!older) box.replaceChildren();
    for (const row of older ? (data?.items || []) : (data?.items || []).slice().reverse()) {
        const item = document.createElement('div'); item.className = 'border-bottom py-2';
        const heading = document.createElement('strong'); heading.textContent = (row.senderName || 'Member') + ' · ' + dateText(row.sentAt);
        const body = document.createElement('div'); body.className = 'text-break'; body.textContent = row.body || '';
        const filesButton = document.createElement('button');
        filesButton.type = 'button'; filesButton.className = 'btn btn-link btn-sm px-0';
        filesButton.textContent = 'Files';
        const filesPanel = document.createElement('div');
        filesButton.addEventListener('click', () => showMessageAttachments(row.id, filesPanel).catch(e => alert(e.message)));
        item.append(heading, body, filesButton, filesPanel);
        if (older) box.prepend(item); else box.append(item);
    }
    el('cwOlderMessages').classList.toggle('d-none', messagePage >= (Number(data?.totalPages) || 1));
    if (!older) {
        try { await api('/api/communication/threads/' + encodeURIComponent(reference) + '/read', 'POST'); await loadThreads(); }
        catch (error) { alert('Messages loaded, but read status could not be updated: ' + error.message); }
    }
}
async function openThread(row) {
    if (!row?.reference || !row.peerUserId) throw new Error('This conversation cannot be opened.');
    active = { reference: row.reference, subject: row.subject || '', peerUserId: Number(row.peerUserId) };
    recipient = { id: Number(row.peerUserId), name: row.peerName || 'Member' };
    messagePage = 1; pendingId = null; pendingPayload = null;
    el('cwConversationTitle').textContent = recipient.name + (active.subject ? ' — ' + active.subject : '');
    el('cwMessageSubject').value = active.subject;
    el('cwMessageBody').value = ''; composeState(); await loadMessages();
}
async function send(event) {
    event.preventDefault();
    if (sending || !recipient) return;
    const body = el('cwMessageBody').value.trim(), subject = el('cwMessageSubject').value.trim();
    if (!body || body.length > 4000 || subject.length > 250) { alert('Enter a valid message.'); return; }
    const payload = { threadReference: active?.reference || null, recipientUserId: recipient.id,
        subject: active ? null : subject, body };
    const fingerprint = JSON.stringify(payload);
    if (fingerprint !== pendingPayload) { pendingPayload = fingerprint; pendingId = crypto.randomUUID(); }
    sending = true; composeState();
    try {
        const result = await api('/api/communication/messages/' + pendingId, 'POST', payload);
        const thread = await api('/api/communication/message-thread-for/' + result.id);
        const row = { reference: thread.reference, subject: thread.subject, peerUserId: recipient.id, peerName: recipient.name };
        pendingId = null; pendingPayload = null;
        el('cwMessageBody').value = '';
        threadPage = 1; await loadThreads(); await openThread(row);
        alert('Message sent.', true);
    } catch (error) {
        if (error.status === 409) { pendingId = null; pendingPayload = null;
            alert('The message may already have been sent. Check the conversation before retrying.'); }
        else alert('Message was not confirmed. Retry without changing the text to reuse the same request ID. ' + error.message);
    } finally { sending = false; composeState(); }
}
export function initMessaging() {
    if (!el('cwMessaging')) return;
    el('cwFindRecipients').addEventListener('click', () => searchRecipients().catch(e => alert(e.message)));
    el('cwRecipientQuery').addEventListener('keydown', event => {
        if (event.key === 'Enter') { event.preventDefault(); searchRecipients().catch(e => alert(e.message)); }
    });
    el('cwNewConversation').addEventListener('click', () => {
        active = null; recipient = null; messageVersion++; pendingId = null; pendingPayload = null;
        el('cwConversationTitle').textContent = 'Find a recipient to start a conversation';
        el('cwConversationMessages').replaceChildren(); el('cwMessageBody').value = ''; el('cwMessageSubject').value = '';
        el('cwOlderMessages').classList.add('d-none'); composeState(); el('cwRecipientQuery').focus();
    });
    el('cwThreadPrev').addEventListener('click', () => { if (threadPage > 1) { threadPage--; loadThreads().catch(e => alert(e.message)); } });
    el('cwThreadNext').addEventListener('click', () => { if (threadPage < threadPages) { threadPage++; loadThreads().catch(e => alert(e.message)); } });
    el('cwOlderMessages').addEventListener('click', () => { messagePage++; loadMessages(true).catch(e => { messagePage--; alert(e.message); }); });
    el('cwMessageForm').addEventListener('submit', send);
    composeState(); loadThreads().catch(e => alert(e.message));
}
