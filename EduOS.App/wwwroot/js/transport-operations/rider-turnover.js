(() => {
  'use strict';
  const get = id => document.getElementById(id);
  if (!get('transportTurnoverPanel')) return;
  const from = get('transportTurnoverFrom'), to = get('transportTurnoverTo');
  const status = get('transportTurnoverStatus'), body = get('transportTurnoverRows');
  const exportButton = get('transportTurnoverExport');
  const today = new Date();
  const current = today.getFullYear() + '-' + String(today.getMonth() + 1).padStart(2, '0');
  const sixMonths = new Date(today.getFullYear(), today.getMonth() - 5, 1);
  from.value = sixMonths.getFullYear() + '-' + String(sixMonths.getMonth() + 1).padStart(2, '0');
  to.value = current; from.max = current; to.max = current;
  let serial = 0, lastFilter = '';
  function filters() {
    const a = from.value, b = to.value;
    if (!/^\d{4}-\d{2}$/.test(a) || !/^\d{4}-\d{2}$/.test(b) || a > b || b > current ||
        (Number(b.slice(0, 4)) - Number(a.slice(0, 4))) * 12 +
        Number(b.slice(5)) - Number(a.slice(5)) >= 12)
      throw Error('Choose one to twelve months ending no later than the current month.');
    const q = new URLSearchParams({ fromMonth: a + '-01', toMonth: b + '-01' });
    const campus = get('transportTurnoverCampus')?.value;
    if (campus) q.set('campusId', campus);
    return q.toString();
  }
  function clear() {
    ++serial; lastFilter = ''; exportButton.disabled = true;
    body.replaceChildren();
    status.textContent = 'Filters changed. Run report again.';
  }
  function addCell(tr, value) {
    const td = document.createElement('td');
    td.textContent = value === null || value === undefined ? '<5 (suppressed)' : String(value);
    tr.append(td);
  }
  async function load(event) {
    event?.preventDefault();
    let query;
    try { query = filters(); } catch (error) { clear(); status.textContent = error.message; return; }
    const ticket = ++serial; lastFilter = ''; exportButton.disabled = true;
    status.textContent = 'Loading rider turnover…'; body.replaceChildren();
    try {
      const response = await fetch('/api/transport/rider-turnover?' + query, {
        credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
      });
      const result = await response.json();
      if (ticket !== serial || query !== filters()) return;
      if (!response.ok || !result.success || !Array.isArray(result.data?.months))
        throw Error(result.message || 'Turnover report unavailable.');
      for (const month of result.data.months) {
        const tr = document.createElement('tr');
        addCell(tr, month.month);
        addCell(tr, month.assignmentStarts);
        addCell(tr, month.assignmentClosures);
        body.append(tr);
      }
      lastFilter = query; exportButton.disabled = false;
      status.textContent = 'Monthly assignment starts and closures; counts 1–4 suppressed.';
    } catch (error) {
      if (ticket === serial) status.textContent = error.message;
    }
  }
  get('transportTurnoverFilter').addEventListener('submit', load);
  exportButton.addEventListener('click', async () => {
    let query;
    try { query = filters(); } catch (error) { clear(); status.textContent = error.message; return; }
    if (!lastFilter || lastFilter !== query) return;
    const ticket = serial; exportButton.disabled = true;
    try {
      const response = await fetch('/api/transport/rider-turnover/export?' + query, {
        credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' }
      });
      if (!response.ok) {
        const result = await response.json().catch(() => null);
        throw Error(result?.message || 'CSV export unavailable.');
      }
      const blob = await response.blob();
      if (ticket !== serial || lastFilter !== query || filters() !== query)
        throw Error('Filters changed during export. Run report again.');
      const url = URL.createObjectURL(blob), link = document.createElement('a');
      link.href = url; link.download = 'transport-rider-turnover.csv';
      document.body.append(link); link.click(); link.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (error) { if (ticket === serial) status.textContent = error.message; }
    finally { exportButton.disabled = !lastFilter || lastFilter !== filters(); }
  });
  from.addEventListener('change', clear);
  to.addEventListener('change', clear);
  get('transportTurnoverCampus')?.addEventListener('change', clear);
  window.addEventListener('transport:assignments-changed', clear);
  async function loadCampuses() {
    try {
      const response = await fetch('/api/transport/route-setup/campuses', {
        credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
      });
      const result = await response.json().catch(() => null);
      if (!response.ok || !result?.success) throw Error(result?.message || 'Campus choices unavailable.');
      for (const item of result.data || []) {
        const option = document.createElement('option');
        option.value = String(item.id); option.textContent = String(item.name);
        get('transportTurnoverCampus').append(option);
      }
    } catch (error) { status.textContent = error.message; }
  }
  loadCampuses().then(() => load());
})();
