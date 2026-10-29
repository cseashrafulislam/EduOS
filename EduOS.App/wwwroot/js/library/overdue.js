(() => {
'use strict';
const root = document.getElementById('libraryOverdueApp');
if (!root) return;
const el = id => document.getElementById(id);
const state = { page: 1, pages: 1, request: 0 };
const alert = message => {
 const node = el('libraryOverdueAlert');
 node.className = 'alert alert-danger';
 node.textContent = message;
};
function query() {
 const campus = el('libraryOverdueCampus').value.trim();
 if (campus && !/^[1-9]\d*$/.test(campus)) throw Error('Campus ID must be a positive whole number.');
 const params = new URLSearchParams({
  search: el('libraryOverdueSearch').value.trim(),
  page: String(state.page), pageSize: '20'
 });
 if (campus) params.set('campusId', campus);
 return params;
}
function cell(row, value) {
 const td = document.createElement('td');
 td.textContent = value == null || value === '' ? '—' : String(value);
 row.append(td);
}
async function load() {
 const seq = ++state.request;
 let params;
 try { params = query(); }
 catch (e) { alert(e.message); return; }
 const body = el('libraryOverdueRows');
 body.replaceChildren();
 const loading = document.createElement('tr');
 cell(loading, 'Loading…'); loading.firstChild.colSpan = 8; body.append(loading);
 try {
  const response = await fetch('/api/library/reports/overdue?' + params, {
   credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
  });
  const payload = await response.json();
  if (!response.ok || !payload?.success) throw Error(payload?.message || 'Report unavailable.');
  if (seq !== state.request) return;
  const data = payload.data;
  body.replaceChildren();
  for (const item of data?.items || []) {
   const row = document.createElement('tr');
   [item.campusId, item.branchName, item.branchCode, item.totalOverdue,
    item.days1To7, item.days8To30, item.days31Plus, item.oldestDueDate].forEach(v => cell(row, v));
   body.append(row);
  }
  if (!body.children.length) {
   const row = document.createElement('tr');
   cell(row, 'No overdue loans in the selected branches.'); row.firstChild.colSpan = 8; body.append(row);
  }
  state.pages = Math.max(1, Number(data?.totalPages || 1));
  el('libraryOverdueCount').textContent = String(data?.totalCount || 0) + ' branch(es)';
  el('libraryOverduePage').textContent = 'Page ' + state.page + ' / ' + state.pages;
  el('libraryOverduePrevious').disabled = state.page <= 1;
  el('libraryOverdueNext').disabled = state.page >= state.pages;
  el('libraryOverdueAlert').className = 'd-none';
 } catch (e) {
  if (seq === state.request) {
   body.replaceChildren();
   const row = document.createElement('tr'); cell(row, 'Report unavailable.'); row.firstChild.colSpan = 8; body.append(row);
   alert(e.message);
  }
 }
}
async function exportPage() {
 let params;
 try { params = query(); } catch (e) { alert(e.message); return; }
 params.set('language', document.documentElement.lang?.toLowerCase().startsWith('bn') ? 'bn-BD' : 'en-BD');
 const button = el('libraryOverdueCsv');
 button.disabled = true;
 try {
  const response = await fetch('/api/library/reports/overdue/export?' + params, {
   credentials: 'same-origin', cache: 'no-store'
  });
  if (!response.ok) {
   const payload = await response.json().catch(() => null);
   throw Error(payload?.message || 'Export unavailable.');
  }
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement('a');
  link.href = url; link.download = 'library-overdue-page.csv';
  document.body.append(link); link.click(); link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
 } catch (e) { alert(e.message); }
 finally { button.disabled = false; }
}
el('libraryOverdueFilters').addEventListener('submit', e => { e.preventDefault(); state.page = 1; load(); });
el('libraryOverduePrevious').addEventListener('click', () => { if (state.page > 1) { state.page--; load(); } });
el('libraryOverdueNext').addEventListener('click', () => { if (state.page < state.pages) { state.page++; load(); } });
el('libraryOverdueCsv').addEventListener('click', exportPage);
load();
})();
