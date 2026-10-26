(() => {
    'use strict';
    const root = document.getElementById('transportCampusAssignmentPanel');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const base = '/api/transport/campus-assignment-review';
    const node = (tag, value) => { const result = document.createElement(tag); result.textContent = String(value ?? ''); return result; };
    function message(value, ok) {
        const panel = el('transportCampusAssignmentAlert');
        panel.className = 'alert alert-' + (ok ? 'success' : 'danger');
        panel.textContent = value;
    }
    async function api(path, options = {}) {
        const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', ...(options.body ? {
                'Content-Type': 'application/json', RequestVerificationToken: token
            } : {}) }, ...options });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Campus review failed.');
        return result.data;
    }
    async function loadCampuses() {
        const campuses = await api('/api/transport/route-setup/campuses');
        const select = el('transportCampusAssignmentTargetCampus');
        select.replaceChildren(new Option('Select a reviewed campus', ''));
        (campuses || []).forEach(item => select.append(new Option(item.name, String(item.id))));
    }
    async function load() {
        const data = await api(base);
        const tbody = el('transportCampusAssignmentRows'); tbody.replaceChildren();
        (data.rows || []).forEach(item => {
            const tr = document.createElement('tr');
            tr.append(node('td', item.kind === 'route' ? 'Route' : 'Vehicle'));
            tr.append(node('td', item.label));
            tr.append(node('td', item.linkedRecords));
            const action = document.createElement('td');
            if (!item.canApply) {
                action.append(node('span', 'Blocked — Platform / Finance review required'));
            } else {
                const button = node('button', 'Review & apply');
                button.type = 'button'; button.className = 'btn btn-outline-primary btn-sm';
                button.addEventListener('click', async () => {
                    const campusId = Number(el('transportCampusAssignmentTargetCampus').value);
                    const campusText = el('transportCampusAssignmentTargetCampus').selectedOptions[0]?.textContent || '';
                    if (!Number.isSafeInteger(campusId) || campusId <= 0) {
                        message('Choose and verify the target campus first.', false); return;
                    }
                    if (!confirm('Confirm that you verified the original campus for ' + item.label +
                        ' and approve assignment to ' + campusText + '? This will be audited.')) return;
                    button.disabled = true;
                    try {
                        await api(base + '/' + encodeURIComponent(item.kind) + '/' +
                            encodeURIComponent(item.reference) + '/apply', {
                            method: 'POST', body: JSON.stringify({ campusId, rowVersion: item.rowVersion,
                                confirmHistoricalCampusReview: true })
                        });
                        message('Campus assignment applied and audited for ' + item.label + '.', true);
                        await load();
                        window.dispatchEvent(new CustomEvent('transport:vehicles-changed'));
                    } catch (error) { message(error.message, false); }
                    finally { button.disabled = false; }
                });
                action.append(button);
            }
            tr.append(action); tbody.append(tr);
        });
        if (!data.rows?.length) { const td = node('td', 'No historical campus gaps found.');
            td.colSpan = 4; const tr = document.createElement('tr'); tr.append(td); tbody.append(tr); }
        el('transportCampusAssignmentLimitNote').textContent = data.hasMore
            ? 'Showing the earliest 100 routes and 100 vehicles. Apply reviewed records, then refresh to load the next group.'
            : 'Every listed record requires a separate verification before approval.';
    }
    el('transportCampusAssignmentRefresh').addEventListener('click', () => load().catch(e => message(e.message, false)));
    Promise.all([loadCampuses(), load()]).catch(e => message(e.message, false));
})();
