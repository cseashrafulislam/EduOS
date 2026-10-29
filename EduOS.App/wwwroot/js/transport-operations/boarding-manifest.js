(() => {
'use strict';
const el = id => document.getElementById(id);
if (!el('transportBoardingManifestPanel')) return;
const endpoint = '/api/transport/boarding-manifest';
const fields = [['transportBoardingManifestCampus','campusId'],
 ['transportBoardingManifestRoute','routeReference'],['transportBoardingManifestVehicle','vehicleReference'],
 ['transportBoardingManifestSearch','search']];
const date = el('transportBoardingManifestDate'), attention = el('transportBoardingManifestAttention');
const rows = el('transportBoardingManifestRows'), status = el('transportBoardingManifestStatus');
const prev = el('transportBoardingManifestPrev'), next = el('transportBoardingManifestNext');
const exportButton = el('transportBoardingManifestExport');
const today = () => { const d = new Date(); return [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-'); };
date.value = today(); date.min = date.value;
let page = 1, pages = 1, serial = 0, loadedFilter = null;
function filters() {
 const q = new URLSearchParams({operatingDate:date.value,attentionOnly:String(attention.checked)});
 for (const [id,key] of fields) { const value = el(id).value.trim(); if(value) q.set(key,value); }
 return q.toString();
}
function invalidate() {
 serial++; loadedFilter = null; exportButton.disabled = true; prev.disabled = true; next.disabled = true;
 rows.replaceChildren(); el('transportBoardingManifestPage').textContent = '';
 status.textContent = 'Filters changed. Apply to refresh.';
}
function cell(value) { const td = document.createElement('td'); td.textContent = value == null ? '—' : String(value); return td; }
async function load() {
 invalidate();
 if (!date.value || date.value < today()) { status.textContent = 'Choose today or a future date.'; return; }
 const request = serial, snapshot = filters(), query = new URLSearchParams(snapshot);
 query.set('page',String(page)); query.set('pageSize','25');
 status.textContent = 'Loading authorized pickup/drop roster…';
 try {
  const response = await fetch(endpoint+'?'+query,{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
  const body = await response.json().catch(() => null);
  if (!response.ok || !body?.success || !body.data) throw Error(body?.message || 'Roster unavailable.');
  if (request !== serial || snapshot !== filters()) return;
  const data = body.data, items = Array.isArray(data.items) ? data.items : [];
  for (const item of items) {
   const tr = document.createElement('tr');
   for (const value of [item.campusName,item.studentCode,item.routeName,item.vehicleNumber,
    item.pickupStop,item.dropStop,item.readiness]) tr.append(cell(value));
   rows.append(tr);
  }
  if (!items.length) { const tr = document.createElement('tr'), td = cell('No matching reservations'); td.colSpan = 7; tr.append(td); rows.append(tr); }
  pages = Math.max(1,Math.ceil((Number(data.totalCount)||0)/25));
  el('transportBoardingManifestPage').textContent = 'Page '+page+' of '+pages+' · '+data.totalCount+' reservations';
  prev.disabled = page <= 1; next.disabled = page >= pages;
  loadedFilter = snapshot; exportButton.disabled = !items.length;
  status.textContent = 'Planning preview only; verify readiness before operating.';
 } catch (error) { if (request === serial) { invalidate(); status.textContent = error.message; } }
}
async function getOptions(url,id,key,label) {
 const response = await fetch(url,{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
 const body = await response.json().catch(() => null);
 if (!response.ok || !body?.success || !Array.isArray(body.data)) throw Error('Roster filters unavailable.');
 const select = el(id);
 for (const item of body.data) if (key(item)) select.add(new Option(String(label(item)),String(key(item))));
}
el('transportBoardingManifestFilters').addEventListener('submit',e=>{e.preventDefault();page=1;load();});
for (const id of ['transportBoardingManifestDate','transportBoardingManifestCampus',
 'transportBoardingManifestRoute','transportBoardingManifestVehicle','transportBoardingManifestSearch'])
 el(id).addEventListener(id.endsWith('Search')?'input':'change',()=>{page=1;invalidate();});
attention.addEventListener('change',()=>{page=1;invalidate();});
prev.addEventListener('click',()=>{page=Math.max(1,page-1);load();});
next.addEventListener('click',()=>{page=Math.min(pages,page+1);load();});
exportButton.addEventListener('click',async()=>{
 const snapshot=filters(),request=serial;
 if (loadedFilter===null || loadedFilter!==snapshot) return;
 exportButton.disabled=true;
 try {
  const response=await fetch(endpoint+'/export?'+snapshot,{credentials:'same-origin',cache:'no-store',headers:{Accept:'text/csv'}});
  if(!response.ok){const body=await response.json().catch(()=>null);throw Error(body?.message||'CSV export failed.');}
  const blob=await response.blob();
  if(request!==serial||snapshot!==filters()||loadedFilter!==snapshot)throw Error('Filters changed; retry export.');
  const url=URL.createObjectURL(blob),a=document.createElement('a');
  a.href=url;a.download='transport-boarding-manifest.csv';document.body.append(a);a.click();a.remove();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
 }catch(error){status.textContent=error.message;}
 finally{exportButton.disabled=loadedFilter===null||loadedFilter!==filters();}
});
(async()=>{
 const results=await Promise.allSettled([
  getOptions('/api/transport/route-setup/campuses','transportBoardingManifestCampus',x=>x.id,x=>x.name),
  getOptions('/api/transport/routes','transportBoardingManifestRoute',x=>x.reference,x=>x.name),
  getOptions('/api/transport/vehicles','transportBoardingManifestVehicle',x=>x.reference,x=>x.vehicleNumber)
 ]);
 if(results.some(x=>x.status==='rejected'))status.textContent='Some filters could not be loaded.';
 await load();
})();
})();
