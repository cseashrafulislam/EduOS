(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const channels = { 1: 'In-app', 2: 'Email', 3: 'SMS', 4: 'Push' };
    const states = { 1: 'Pending', 2: 'Processing', 3: 'Sent', 4: 'Delivered', 5: 'Failed' };
    let page = 1, pages = 1, requestSequence = 0;
    const cell = value => {
        const td = document.createElement('td');
        td.textContent = value == null || value === '' ? '—' : String(value);
        return td;
    };
    const date = value => value && !Number.isNaN(Date.parse(value)) ? new Date(value).toLocaleString() : '—';
    const alert = message => {
        const node = el('deliveryAlert');
        node.textContent = message;
        node.classList.remove('d-none');
    };
    const filters = () => {
        const params = new URLSearchParams();
        if (el('deliveryChannel').value) params.set('channel', el('deliveryChannel').value);
        if (el('deliveryState').value) params.set('state', el('deliveryState').value);
        return params;
    };
    let exporting = false;
    async function exportCsv() {
        if (exporting) return;
        exporting = true;
        const button = el('deliveryExport');
        button.disabled = true;
        el('deliveryAlert').classList.add('d-none');
        try {
            const response = await fetch('/api/communication-admin/deliveries/export?' + filters().toString(), {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' }
            });
            if (!response.ok) {
                const result = await response.json().catch(() => null);
                throw Error(result?.message || 'Unable to export delivery history (' + response.status + ').');
            }
            const blob = await response.blob();
            const url = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = url; link.download = 'communication-deliveries.csv';
            document.body.append(link);
            try { link.click(); } finally { link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
        } catch (error) { alert(error.message || 'Delivery export failed.'); }
        finally { exporting = false; button.disabled = false; }
    }
    async function load() {
        const sequence = ++requestSequence;
        el('deliveryAlert').classList.add('d-none');
        const params = filters();
        params.set('page', String(page)); params.set('pageSize', '20');
        try {
            const response = await fetch('/api/communication-admin/deliveries?' + params,
                { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success) throw Error(result?.message || 'Delivery history could not be loaded.');
            if (sequence !== requestSequence) return;
            const data = result.data;
            if (!data || !Array.isArray(data.items)) throw Error('Invalid delivery history response.');
            pages = Math.max(1, Number(data.totalPages) || 1);
            const tbody = el('deliveryRows');
            tbody.replaceChildren();
            for (const row of data.items) {
                const tr = document.createElement('tr');
                for (const value of [row.id, channels[row.channel] || row.channel,
                    row.destination, row.subject, states[row.state] || row.state,
                    row.attemptCount, date(row.sentAt), date(row.deliveredAt), row.failureReason])
                    tr.append(cell(value));
                tbody.append(tr);
            }
            if (!data.items.length) {
                const tr = document.createElement('tr'), td = cell('No deliveries match these filters.');
                td.colSpan = 9; tr.append(td); tbody.append(tr);
            }
            el('deliveryPage').textContent = 'Page ' + page + ' of ' + pages;
            el('deliveryPrev').disabled = page <= 1;
            el('deliveryNext').disabled = page >= pages;
        } catch (error) { if (sequence === requestSequence) alert(error.message); }
    }
    document.addEventListener('DOMContentLoaded', () => {
        if (!el('deliveryRows')) return;
        for (const id of ['deliveryChannel', 'deliveryState'])
            el(id).addEventListener('change', () => { page = 1; load(); });
        el('deliveryRefresh').addEventListener('click', load);
        el('deliveryExport').addEventListener('click', exportCsv);
        el('deliveryPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
        el('deliveryNext').addEventListener('click', () => { if (page < pages) { page++; load(); } });
        load();
    }, { once: true });
})();
