import { el, admin, state, api, cell, dateText, alert, action, pager } from './workspace-core.js?v=1';
import { showNoticeReaders } from './workspace-notice-readers.js?v=1';
async function archiveNotice(row, edit, button) {
    if (!admin || !row?.reference || button.disabled) return;
    if (!window.confirm('Archive this notice? It will no longer be visible to readers; read receipts are retained.')) return;
    button.disabled = true;
    try {
        const path = '/api/communication/notices/' + encodeURIComponent(row.reference) +
            '?rowVersion=' + encodeURIComponent(row.rowVersion || '');
        await api(path, 'DELETE');
        await loadNotices(edit);
        if (state.noticePage > 1 && !state.noticeItems.length) {
            state.noticePage--;
            await loadNotices(edit);
        }
        alert('Notice archived. Historical read receipts are retained.', true);
    } catch (error) {
        alert(error.message);
        if (error.status === 409) await loadNotices(edit).catch(() => {});
    } finally {
        button.disabled = false;
    }
}
export function renderNotices(edit) {
    const tbody = el('cwNoticeRows'); tbody.replaceChildren();
    for (const row of state.noticeItems) {
        const tr = document.createElement('tr');
        [row.title, row.noticeCategoryName, dateText(row.publishAt), dateText(row.expiresAt),
            row.isPublished ? 'Published' : 'Draft'].forEach(x => tr.append(cell(x)));
        const actions = cell('');
        actions.append(action('View', () => {
            const details = document.createElement('tr'), td = cell(row.body);
            td.colSpan = 6; td.style.whiteSpace = 'pre-wrap'; details.append(td);
            if (tr.nextSibling?.dataset?.detail === row.reference) tr.nextSibling.remove();
            else { details.dataset.detail = row.reference; tr.after(details); }
        }));
        if (admin) {
            actions.append(action('Edit', () => edit(row)));
            actions.append(action('Readers', () => showNoticeReaders(row)));
            const archive = action('Archive', () => archiveNotice(row, edit, archive));
            actions.append(archive);
        }
        else actions.append(action('Mark read', async () => {
            try { await api('/api/communication/notices/' + encodeURIComponent(row.reference) + '/read', 'POST');
                alert('Notice marked as read.', true); } catch (error) { alert(error.message); }
        }));
        tr.append(actions); tbody.append(tr);
    }
    if (!state.noticeItems.length) { const tr = document.createElement('tr'), td = cell('No notices found.');
        td.colSpan = 6; tr.append(td); tbody.append(tr); }
}
export async function loadNotices(edit) {
    const generation = ++state.noticeGeneration;
    const params = new URLSearchParams({ page: String(state.noticePage), pageSize: '20' });
    if (admin && el('cwNoticeFilter').value) params.set('published', el('cwNoticeFilter').value);
    const data = await api('/api/communication/notices?' + params);
    if (generation !== state.noticeGeneration) return;
    state.noticeItems = data?.items || []; state.noticePages = Math.max(1, Number(data?.totalPages) || 1);
    pager('notice'); renderNotices(edit);
}
