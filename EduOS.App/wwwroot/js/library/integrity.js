(() => {
'use strict';
const root = document.getElementById('libraryIntegrityApp');
if (!root) return;
const el = id => document.getElementById(id);
const state = { page: 1, pages: 1, request: 0 };
function error(message) {
 const node = el('libraryIntegrityAlert');
 node.className = 'alert alert-danger';
 node.textContent = message;
}
function query() {
 const campus = el('libraryIntegrityCampus').value.trim();
 if (campus && !/^[1-9]\d*$/.test(campus)) throw Error('Campus ID must be a positive whole number.');
 const params = new URLSearchParams({
  accession: el('libraryIntegrityAccession').value.trim(),
  page: String(state.page), pageSize: '20'
 });
 if (campus) params.set('campusId', campus);
 return params;
}
function cell(tr, value) {
 const td = document.createElement('td');
 td.textContent = value == null || value === '' ? '—' : String(value);
 tr.append(td);
}
function empty(message) {
 const body = el('libraryIntegrityRows');
 body.replaceChildren();
 const tr = document.createElement('tr');
 cell(tr, message); tr.firstChild.colSpan = 7; body.append(tr);
}
function discrepancies(item) {
 const flags = [];
 if (item.missingBook) flags.push('Missing book');
 if (item.missingBranch) flags.push('Missing branch');
 if (item.stateMismatch) flags.push('State mismatch');
 if (item.multipleOpenLoans) flags.push('Multiple open loans');
 return flags.join(', ') || '—';
}
async function load() {
 const seq = ++state.request;
 let params;
 try { params = query(); } catch (e) { error(e.message); return; }
 empty('Loading…');
 try {
  const response = await fetch('/api/library/reports/copy-integrity?' + params, {
   credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
  });
  const payload = await response.json();
  if (!response.ok || !payload?.success) throw Error(payload?.message || 'Report unavailable.');
  if (seq !== state.request) return;
  const data = payload.data;
  const body = el('libraryIntegrityRows');
  body.replaceChildren();
  for (const item of data?.items || []) {
   const tr = document.createElement('tr');
   [item.accessionNumber, item.bookTitle, item.campusId, item.branchCode,
    item.copyState, item.openLoanCount, discrepancies(item)].forEach(value => cell(tr, value));
   body.append(tr);
  }
  if (!body.children.length) empty('No copy discrepancies in the selected scope.');
  state.pages = Math.max(1, Number(data?.totalPages || 1));
  el('libraryIntegrityCount').textContent = String(data?.totalCount || 0) + ' discrepancy copy/copies';
  el('libraryIntegrityPage').textContent = 'Page ' + state.page + ' / ' + state.pages;
  el('libraryIntegrityPrevious').disabled = state.page <= 1;
  el('libraryIntegrityNext').disabled = state.page >= state.pages;
  el('libraryIntegrityAlert').className = 'd-none';
 } catch (e) {
  if (seq === state.request) { empty('Report unavailable.'); error(e.message); }
 }
}
async function exportPage() {
 let params;
 try { params = query(); } catch (e) { error(e.message); return; }
 params.set('language', document.documentElement.lang?.toLowerCase().startsWith('bn') ? 'bn-BD' : 'en-BD');
 const button = el('libraryIntegrityCsv');
 button.disabled = true;
 try {
  const response = await fetch('/api/library/reports/copy-integrity/export?' + params, {
   credentials: 'same-origin', cache: 'no-store'
  });
  if (!response.ok) {
   const payload = await response.json().catch(() => null);
   throw Error(payload?.message || 'Export unavailable.');
  }
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement('a');
  link.href = url; link.download = 'library-copy-integrity-page.csv';
  document.body.append(link); link.click(); link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
 } catch (e) { error(e.message); }
 finally { button.disabled = false; }
}
el('libraryIntegrityFilters').addEventListener('submit', e => { e.preventDefault(); state.page = 1; load(); });
el('libraryIntegrityPrevious').addEventListener('click', () => { if (state.page > 1) { state.page--; load(); } });
el('libraryIntegrityNext').addEventListener('click', () => { if (state.page < state.pages) { state.page++; load(); } });
el('libraryIntegrityCsv').addEventListener('click', exportPage);
load();
})();
