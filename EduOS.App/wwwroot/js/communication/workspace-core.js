'use strict';
export const el = id => document.getElementById(id);
export const root = el('communicationWorkspace');
export const admin = root?.dataset.canManage === 'true';
export const state = { noticePage: 1, noticePages: 1, noticeGeneration: 0, noticeItems: [],
    notificationPage: 1, notificationPages: 1, notificationGeneration: 0, audiences: [], saving: false };
export const cell = value => { const td = document.createElement('td'); td.textContent = value == null ? '—' : String(value); return td; };
export const dateText = value => value ? new Date(value).toLocaleString() : '—';
export const localInput = value => {
    const date = value ? new Date(value) : new Date();
    return Number.isFinite(date.getTime()) ? new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16) : '';
};
export const alert = (message, good = false) => {
    const box = el('cwAlert'); box.className = 'alert alert-' + (good ? 'success' : 'danger');
    box.textContent = message; box.focus();
};
export async function api(path, method = 'GET', body) {
    const headers = { Accept: 'application/json' };
    if (method !== 'GET') {
        const csrf = document.querySelector('meta[name="request-verification-token"]')?.content;
        if (!csrf) throw new Error('Security token missing. Reload the page.');
        headers.RequestVerificationToken = csrf;
    }
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(path, { method, credentials: 'same-origin', cache: 'no-store', headers,
        ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
    const result = await response.json().catch(() => null);
    if (!response.ok || !result?.success) { const error = new Error(result?.message || 'Communication request failed.'); error.status = response.status; throw error; }
    return result.data;
}
export function action(text, click, disabled = false) {
    const button = document.createElement('button'); button.type = 'button';
    button.className = 'btn btn-outline-primary btn-sm me-1'; button.textContent = text;
    button.disabled = disabled; button.addEventListener('click', click); return button;
}
export function pager(kind) {
    const page = state[kind + 'Page'], pages = state[kind + 'Pages'];
    el('cw' + (kind === 'notice' ? 'Notice' : 'Notification') + 'Page').textContent = `Page ${page} of ${pages}`;
    el('cw' + (kind === 'notice' ? 'Notice' : 'Notification') + 'Prev').disabled = page <= 1;
    el('cw' + (kind === 'notice' ? 'Notice' : 'Notification') + 'Next').disabled = page >= pages;
}
