(() => {
'use strict';
const el = id => document.getElementById(id);
const panel = el('transportVehicleCasesPanel');
if (!panel) return;
const endpoint = '/api/transport/vehicle-cases';
const form = el('transportVehicleCaseCreate');
const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
const status = el('transportVehicleCaseStatus');
const tbody = el('transportVehicleCaseRows');
const exportButton = el('transportVehicleCaseExport');
const printButton = el('transportVehicleCasePrint');
const prev = el('transportVehicleCasePrev'), next = el('transportVehicleCaseNext');
const date = el('transportVehicleCaseDate');
const today = () => { const d = new Date(); return [d.getFullYear(), String(d.getMonth()+1).padStart(2,'0'), String(d.getDate()).padStart(2,'0')].join('-'); };
date.value = today(); date.max = date.value;
let page = 1, pages = 1, generation = 0, loaded = '', items = [], requestKey = null, selectedCase = null;
const filters = () => new URLSearchParams({state:el('transportVehicleCaseState').value, search:el('transportVehicleCaseSearch').value.trim()}).toString();
const cell = value => { const td = document.createElement('td'); td.textContent = value == null || value === '' ? '—' : String(value); return td; };
async function api(url, method='GET', body) {
 const headers = {Accept:'application/json'};
 if (body !== undefined) { headers['Content-Type']='application/json'; headers.RequestVerificationToken=token; }
 const response = await fetch(url,{method,headers,credentials:'same-origin',cache:'no-store',body:body === undefined ? undefined : JSON.stringify(body)});
 const data = await response.json().catch(()=>null);
 if (!response.ok || !data?.success) throw Error(data?.message || 'Transport case request failed.');
 return data.data;
}
async function vehicles() {
 try {
  const data = await api('/api/transport/vehicles');
  const select = el('transportVehicleCaseVehicle');
  for (const item of Array.isArray(data) ? data : []) {
   const option = document.createElement('option');
   option.value = item.reference; option.textContent = item.vehicleNumber; select.append(option);
  }
 } catch(error) { status.textContent = error.message; }
}
function invalidate() {
 generation++; loaded=''; items=[]; exportButton.disabled=true; printButton.disabled=true;
 prev.disabled=true; next.disabled=true; tbody.replaceChildren();
 status.textContent='Filters changed. Apply to refresh.';
}
async function load() {
 invalidate();
 const request = generation, query = filters(), url = new URLSearchParams(query);
 url.set('page',String(page)); url.set('pageSize','25');
 status.textContent='Loading authorized cases…';
 try {
  const data = await api(endpoint+'?'+url);
  if (request !== generation || query !== filters()) return;
  items = Array.isArray(data.items) ? data.items : [];
  for (const item of items) {
   const tr = document.createElement('tr');
   for (const value of [item.occurredOn,item.campusName,item.vehicleNumber,item.caseType,item.severity,item.summary,item.state,item.resolution])
    tr.append(cell(value));
   const actions = document.createElement('td');
   if (item.state === 'Open') {
    const button = document.createElement('button');
    button.type='button'; button.className='btn btn-outline-primary btn-sm'; button.textContent='Resolve';
    button.addEventListener('click',()=>resolveCase(item)); actions.append(button);
   }
   tr.append(actions); tbody.append(tr);
  }
  if (!items.length) { const tr=document.createElement('tr'), td=cell('No matching cases'); td.colSpan=9; tr.append(td); tbody.append(tr); }
  pages=Math.max(1,Math.ceil((Number(data.totalCount)||0)/25));
  el('transportVehicleCasePage').textContent='Page '+page+' of '+pages+' · '+data.totalCount+' cases';
  prev.disabled=page<=1; next.disabled=page>=pages;
  loaded=query; exportButton.disabled=!items.length; printButton.disabled=!items.length;
  status.textContent='Operational records only; no finance posting or dispatch approval.';
 } catch(error) { if(request===generation) status.textContent=error.message; }
}
function resolveCase(item) {
 selectedCase = item;
 el('transportVehicleCaseResolution').value = '';
 el('transportVehicleCaseDialog').showModal();
 el('transportVehicleCaseResolution').focus();
}
el('transportVehicleCaseCancel').addEventListener('click',()=>el('transportVehicleCaseDialog').close());
el('transportVehicleCaseDialog').addEventListener('close',()=>{selectedCase=null;});
el('transportVehicleCaseResolve').addEventListener('submit',async event=>{
 event.preventDefault();
 const item=selectedCase, resolution=el('transportVehicleCaseResolution').value.trim();
 if(!item || !resolution || resolution.length>1000) return;
 try {
  await api(endpoint+'/'+encodeURIComponent(item.reference)+'/resolve','POST',{rowVersion:item.rowVersion,resolution});
  el('transportVehicleCaseDialog').close();
  await load();
 } catch(error) { status.textContent=error.message; el('transportVehicleCaseDialog').close(); await load(); }
});
form.addEventListener('submit',async event=>{
 event.preventDefault();
 if(!form.reportValidity()) return;
 const request = {
  clientRequestId: requestKey || (globalThis.crypto?.randomUUID?.() || ''),
  vehicleReference:el('transportVehicleCaseVehicle').value,
  occurredOn:date.value,caseType:el('transportVehicleCaseType').value,
  severity:el('transportVehicleCaseSeverity').value,summary:el('transportVehicleCaseSummary').value.trim()
 };
 if(!request.clientRequestId || !request.vehicleReference || !request.summary) { status.textContent='Complete all required fields.'; return; }
 requestKey=request.clientRequestId;
 try { await api(endpoint,'POST',request); requestKey=null; form.reset(); date.value=today(); status.textContent='Case recorded.'; page=1; await load(); }
 catch(error) { status.textContent=error.message; }
});
el('transportVehicleCaseFilters').addEventListener('submit',event=>{event.preventDefault();page=1;load();});
for (const id of ['transportVehicleCaseState','transportVehicleCaseSearch']) el(id).addEventListener('input',invalidate);
prev.addEventListener('click',()=>{if(page>1){page--;load();}});
next.addEventListener('click',()=>{if(page<pages){page++;load();}});
exportButton.addEventListener('click',async()=>{
 if(loaded !== filters()) return;
 try {
  const response=await fetch(endpoint+'/export?'+loaded,{credentials:'same-origin',cache:'no-store'});
  if(!response.ok) { const body=await response.json().catch(()=>null); throw Error(body?.message || 'Export failed.'); }
  const blob=await response.blob(), url=URL.createObjectURL(blob), anchor=document.createElement('a');
  anchor.href=url;anchor.download='transport-vehicle-cases.csv';anchor.click();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
 } catch(error) {status.textContent=error.message;}
});
printButton.addEventListener('click',async()=>{
 if(loaded !== filters() || !items.length) return;
 try {
  const query = new URLSearchParams(loaded);
  query.set('page',String(page)); query.set('pageSize','25');
  const data = await api(endpoint+'?'+query);
  const current = Array.isArray(data.items) ? data.items : [];
  if(current.length !== items.length || current.some((item,i)=>item.reference!==items[i].reference || item.rowVersion!==items[i].rowVersion))
   throw Error('Cases changed. Refresh before printing.');
  const win=window.open('','_blank','width=900,height=700');
  if(!win) {status.textContent='Allow the print window to open.';return;}
  const doc=win.document;
  doc.title='Transport vehicle cases';
  const heading=doc.createElement('h2');heading.textContent='Transport vehicle cases — page '+page;doc.body.append(heading);
  const table=doc.createElement('table');table.border='1';table.cellPadding='5';
  const head=doc.createElement('tr');
  for(const name of ['Date','Campus','Vehicle','Type','Severity','Summary','Status','Resolution']){const th=doc.createElement('th');th.textContent=name;head.append(th);}
  table.append(head);
  for(const item of current){const tr=doc.createElement('tr');for(const v of [item.occurredOn,item.campusName,item.vehicleNumber,item.caseType,item.severity,item.summary,item.state,item.resolution])tr.append(cell(v));table.append(tr);}
  doc.body.append(table);win.focus();win.print();
 } catch(error) {status.textContent=error.message;}
});

vehicles();load();
})();
