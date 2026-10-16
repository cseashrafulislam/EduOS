(() => {
  'use strict';
  const el = id => document.getElementById(id);
  const host = el('transportRouteStopEditor');
  if (!host) return;
  const form = el('transportRouteStopForm'), rows = el('transportRouteStopRows');
  const url = '/api/transport/route-setup/' + encodeURIComponent(host.dataset.reference) + '/stops';
  const token = form.querySelector('[name="__RequestVerificationToken"]').value;
  let version = '';
  const alert = (message, success = false) => {
    const box = el('transportRouteStopAlert');
    box.className = 'alert alert-' + (success ? 'success' : 'danger');
    box.textContent = message;
  };
  async function request(method, payload) {
    const headers = { Accept: 'application/json' };
    if (payload) { headers['Content-Type'] = 'application/json'; headers.RequestVerificationToken = token; }
    const response = await fetch(url, {
      method, headers, credentials: 'same-origin',
      ...(payload ? { body: JSON.stringify(payload) } : {})
    });
    const json = await response.json();
    if (!response.ok || !json.success) throw new Error(json.message || 'Route update failed.');
    return json.data;
  }
  function renumber() {
    Array.from(rows.children).forEach((row, index) => {
      row.querySelector('span').textContent = String(index + 1) + '.';
    });
  }
  function add(stop) {
    const row = document.createElement('div');
    row.className = 'd-flex gap-2 align-items-center';
    row.dataset.id = stop.id;
    row.dataset.version = stop.rowVersion;
    const number = document.createElement('span');
    const name = document.createElement('input');
    name.className = 'form-control'; name.required = true;
    name.maxLength = 150; name.value = stop.name;
    name.setAttribute('aria-label', 'Stop name');
    row.append(number, name);
    [-1, 1].forEach(direction => {
      const button = document.createElement('button');
      button.type = 'button'; button.className = 'btn btn-outline-secondary btn-sm';
      button.textContent = direction < 0 ? '↑' : '↓';
      button.addEventListener('click', () => {
        if (direction < 0 && row.previousElementSibling) rows.insertBefore(row, row.previousElementSibling);
        if (direction > 0 && row.nextElementSibling) rows.insertBefore(row.nextElementSibling, row);
        renumber();
      });
      row.append(button);
    });
    rows.append(row);
  }
  async function reload() {
    const data = await request('GET');
    version = data.rowVersion;
    el('transportRouteStopName').textContent = data.name;
    rows.replaceChildren();
    data.stops.forEach(add);
    renumber();
    el('transportRouteStopSave').disabled = !rows.children.length;
  }
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (!form.reportValidity()) return;
    const stops = Array.from(rows.children).map(row => ({
      id: Number(row.dataset.id), rowVersion: row.dataset.version,
      name: row.querySelector('input').value.trim()
    }));
    if (stops.some(x => !x.name) || new Set(stops.map(x => x.name.toLowerCase())).size !== stops.length) {
      alert('Stop names must be unique.'); return;
    }
    const button = el('transportRouteStopSave');
    button.disabled = true;
    try {
      await request('PUT', { rowVersion: version, stops });
      await reload();
      alert('Route stops updated.', true);
    } catch (error) { alert(error.message); }
    finally { button.disabled = false; }
  });
  reload().catch(error => alert(error.message));
})();
