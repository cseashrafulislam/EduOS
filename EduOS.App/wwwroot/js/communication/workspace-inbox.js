import { el, state, api, cell, dateText, alert, action, pager } from './workspace-core.js?v=1';
export async function loadNotifications() {
    const generation = ++state.notificationGeneration;
    const params = new URLSearchParams({ page: String(state.notificationPage), pageSize: '20' });
    const data = await api('/api/communication/notifications?' + params);
    if (generation !== state.notificationGeneration) return;
    state.notificationPages = Math.max(1, Number(data?.totalPages) || 1);
    const tbody = el('cwNotificationRows'); tbody.replaceChildren();
    for (const row of data?.items || []) {
        const tr = document.createElement('tr');
        [dateText(row.createdAt), row.title, row.body, row.isRead ? 'Read' : 'Unread'].forEach(x => tr.append(cell(x)));
        const actions = cell('');
        if (!row.isRead) actions.append(action('Mark read', async () => {
            try { await api('/api/communication/notifications/' + row.id + '/read', 'POST');
                await loadNotifications(); alert('Notification marked as read.', true); }
            catch (error) { alert(error.message); }
        }));
        tr.append(actions); tbody.append(tr);
    }
    if (!data?.items?.length) { const tr = document.createElement('tr'), td = cell('No notifications found.');
        td.colSpan = 5; tr.append(td); tbody.append(tr); }
    pager('notification');
}

export function initNotificationInbox() {
    const button = el('cwMarkAllNotificationsRead');
    if (!button) return;
    button.addEventListener('click', async () => {
        if (!window.confirm('Mark all your unread notifications as read?')) return;
        button.disabled = true;
        try {
            const count = await api('/api/communication/notifications/read-all', 'POST');
            state.notificationPage = 1;
            await loadNotifications();
            alert(`${Number(count) || 0} notification(s) marked as read.`, true);
        } catch (error) { alert(error.message); }
        finally { button.disabled = false; }
    });
}
