(() => {
  'use strict';
  const get = id => document.getElementById(id);
  if (!get('hostelTurnoverPanel')) return;
  const from = get('hostelTurnoverFrom'), to = get('hostelTurnoverTo');
  const status = get('hostelTurnoverStatus'), body = get('hostelTurnoverRows');
  const exportButton = get('hostelTurnoverExport');
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
    const campus = get('hostelCapacityCampus')?.value;
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
    status.textContent = 'Loading monthly turnover…'; body.replaceChildren();
    try {
      const response = await fetch('/api/hostel/turnover-report?' + query, {
        credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
      });
      const result = await response.json();
      if (ticket !== serial || query !== filters()) return;
      if (!response.ok || !result.success || !Array.isArray(result.data?.months))
        throw Error(result.message || 'Turnover report unavailable.');
      for (const month of result.data.months) {
        const tr = document.createElement('tr');
        addCell(tr, month.month);
        addCell(tr, month.allocationStarts);
        addCell(tr, month.allocationClosures);
        body.append(tr);
      }
      lastFilter = query; exportButton.disabled = false;
      status.textContent = 'Monthly allocation starts and closures; counts 1–4 suppressed.';
    } catch (error) {
      if (ticket === serial) status.textContent = error.message;
    }
  }
  get('hostelTurnoverFilter').addEventListener('submit', load);
  exportButton.addEventListener('click', () => {
    let query;
    try { query = filters(); } catch { clear(); return; }
    if (query === lastFilter) window.location.assign('/api/hostel/turnover-report/export?' + query);
  });
  from.addEventListener('change', clear);
  to.addEventListener('change', clear);
  get('hostelCapacityCampus')?.addEventListener('change', clear);
  window.addEventListener('hostel:allocations-changed', clear);
  load();
})();
