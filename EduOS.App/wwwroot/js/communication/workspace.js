import { el, root, admin, state, alert } from './workspace-core.js?v=1';
import { loadNotices } from './workspace-notice-list.js?v=1';
import { initNoticeReaders } from './workspace-notice-readers.js?v=1';
import { edit, initNoticeEditor } from './workspace-notice-editor.js?v=1';
import { loadNotifications } from './workspace-inbox.js?v=1';
import { initMessaging } from './workspace-messages.js?v=1';
import { initPreferences } from './workspace-preferences.js?v=1';
import { initDevices } from './workspace-devices.js?v=1';
if (root) {
    if (admin) { initNoticeEditor(); initNoticeReaders(); }
    for (const [kind, loader] of [['Notice', () => loadNotices(edit)], ['Notification', loadNotifications]]) {
        const key = kind.toLowerCase();
        el('cw' + kind + 'Prev').addEventListener('click', () => {
            if (state[key + 'Page'] > 1) { state[key + 'Page']--; loader().catch(error => alert(error.message)); }
        });
        el('cw' + kind + 'Next').addEventListener('click', () => {
            if (state[key + 'Page'] < state[key + 'Pages']) { state[key + 'Page']++; loader().catch(error => alert(error.message)); }
        });
    }
    loadNotices(edit).catch(error => alert(error.message));
    loadNotifications().catch(error => alert(error.message));
    initMessaging();
    initPreferences();
    initDevices();
}
