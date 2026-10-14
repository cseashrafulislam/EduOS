import { el, api, alert, action, cell } from './workspace-core.js?v=1';
const channels = { 1: 'In-app', 2: 'Email', 3: 'SMS', 4: 'Push' };
const channelId = value => typeof value === 'number' ? value : ({ inapp: 1, email: 2, sms: 3, push: 4 })[String(value).toLowerCase()] || Number(value);
const eventCode = value => String(value || '').trim().toUpperCase();
let items = [], version = 0, busy = false, editing = null;
function lock() {
    el('cwPrefSave').disabled = busy;
    el('cwPrefRefresh').disabled = busy;
    el('cwPrefCancel').disabled = busy;
    el('cwPrefRows').querySelectorAll('button').forEach(button => { button.disabled = busy; });
}
function reset() {
    editing = null; el('cwPrefForm').reset();
    el('cwPrefChannel').disabled = false; el('cwPrefEvent').readOnly = false;
    el('cwPrefEditing').textContent = 'Create a preference or edit an existing row.';
}
function edit(row) {
    editing = { channel: channelId(row.channel), code: eventCode(row.eventCode), rowVersion: row.rowVersion };
    el('cwPrefChannel').value = String(editing.channel); el('cwPrefEvent').value = editing.code;
    el('cwPrefEnabled').checked = !!row.isEnabled;
    el('cwPrefChannel').disabled = true; el('cwPrefEvent').readOnly = true;
    el('cwPrefEditing').textContent = 'Editing ' + editing.code + '. Clear to create a different preference.';
}
function render() {
    const body = el('cwPrefRows'); body.replaceChildren();
    for (const row of items) {
        const tr = document.createElement('tr');
        tr.append(cell(channels[channelId(row.channel)] || 'Unknown'), cell(row.eventCode), cell(row.isEnabled ? 'Enabled' : 'Disabled'));
        const actions = cell('');
        actions.append(action('Edit', () => edit(row), busy));
        actions.append(action(row.isEnabled ? 'Disable' : 'Enable', () => save({
            channel: channelId(row.channel), eventCode: eventCode(row.eventCode),
            isEnabled: !row.isEnabled, rowVersion: row.rowVersion
        }), busy));
        tr.append(actions); body.append(tr);
    }
    if (!items.length) {
        const tr = document.createElement('tr'), td = cell('No preferences saved. Add one above.');
        td.colSpan = 4; tr.append(td); body.append(tr);
    }
    lock();
}
async function load() {
    const request = ++version;
    try {
        const data = await api('/api/communication/preferences');
        if (request !== version) return;
        items = Array.isArray(data) ? data : [];
        render();
    } catch (error) { if (request === version) alert(error.message); }
}
async function save(payload) {
    if (busy) return;
    busy = true; lock();
    try {
        await api('/api/communication/preferences', 'PUT', payload);
        reset(); await load(); alert('Notification preference saved.', true);
    } catch (error) {
        alert(error.status === 409 ? 'Preference changed elsewhere. Refresh and retry.' : error.message);
        if (error.status === 409) { reset(); await load(); }
    } finally { busy = false; lock(); }
}
export function initPreferences() {
    if (!el('cwPrefForm')) return;
    el('cwPrefForm').addEventListener('submit', event => {
        event.preventDefault();
        if (busy) return;
        const code = eventCode(el('cwPrefEvent').value);
        const channel = Number(el('cwPrefChannel').value);
        if (!/^[A-Z][A-Z0-9_.-]{1,99}$/.test(code) || !channels[channel]) {
            alert('Enter a valid event code and channel.'); return;
        }
        if (!editing && items.some(row => channelId(row.channel) === channel && eventCode(row.eventCode) === code)) {
            alert('This preference exists. Use Edit on its row.'); return;
        }
        save({ channel, eventCode: code, isEnabled: el('cwPrefEnabled').checked, rowVersion: editing?.rowVersion || null });
    });
    el('cwPrefCancel').addEventListener('click', reset);
    el('cwPrefRefresh').addEventListener('click', () => { if (!busy) { reset(); load(); } });
    load();
}
