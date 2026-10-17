(() => {
'use strict';
const el=id=>document.getElementById(id);
if(!el('transportDriverReadinessPanel'))return;
const endpoint='/api/transport/driver-readiness';
const campus=el('transportDriverReadinessCampus'),search=el('transportDriverReadinessSearch');
const state=el('transportDriverReadinessState'),days=el('transportDriverReadinessDays');
const status=el('transportDriverReadinessStatus'),rows=el('transportDriverReadinessRows');
const exportButton=el('transportDriverReadinessExport');
const previous=el('transportDriverReadinessPrev'),next=el('transportDriverReadinessNext');
let page=1,pages=1,sequence=0,loadedFilter=null;
const filters=()=>{
 const q=new URLSearchParams({state:state.value,withinDays:days.value});
 if(campus.value)q.set('campusId',campus.value);
 if(search.value.trim())q.set('search',search.value.trim());
 return q.toString();
};
const cell=value=>{const td=document.createElement('td');td.textContent=value==null?'—':String(value);return td;};
function invalidate(){
 sequence++;loadedFilter=null;exportButton.disabled=true;
 status.textContent='Filters changed. Apply filters to refresh.';
}
async function load(){
 loadedFilter=null;exportButton.disabled=true;
 const request=++sequence,snapshot=filters(),q=new URLSearchParams(snapshot);
 q.set('page',String(page));q.set('pageSize','15');
 status.textContent='Loading driver readiness…';
 try{
  const response=await fetch(endpoint+'?'+q,{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
  const body=await response.json().catch(()=>null);
  if(!response.ok||!body?.success||!body.data)throw Error(body?.message||'Driver readiness unavailable.');
  if(request!==sequence||snapshot!==filters())return;
  const data=body.data;rows.replaceChildren();
  for(const item of data.items||[]){
   const tr=document.createElement('tr');
   [item.campusName,item.vehicleNumber,item.activeRiders+' / '+item.capacity,item.currentRouteLinks,
    item.currentDriverLinks,item.licenseExpiresOn,item.readinessState].forEach(value=>tr.append(cell(value)));
   rows.append(tr);
  }
  if(!data.items?.length){const tr=document.createElement('tr'),td=cell('No matching vehicles');td.colSpan=7;tr.append(td);rows.append(tr);}
  pages=Math.max(1,Number(data.totalPages)||1);
  el('transportDriverReadinessPage').textContent='Page '+page+' of '+pages+' · '+data.totalCount+' vehicles';
  previous.disabled=page<=1;next.disabled=page>=pages;
  loadedFilter=snapshot;exportButton.disabled=!data.items?.length;
  status.textContent='Live readiness snapshot; refresh before dispatch decisions.';
 }catch(error){if(request===sequence){rows.replaceChildren();status.textContent=error.message;previous.disabled=true;next.disabled=true;}}
}
el('transportDriverReadinessFilters').addEventListener('submit',event=>{event.preventDefault();page=1;load();});
search.addEventListener('input',invalidate);
campus.addEventListener('change',invalidate);
state.addEventListener('change',invalidate);
days.addEventListener('change',invalidate);
previous.addEventListener('click',()=>{page=Math.max(1,page-1);load();});
next.addEventListener('click',()=>{page=Math.min(pages,page+1);load();});
exportButton.addEventListener('click',async()=>{
 const snapshot=filters(),request=sequence;
 if(loadedFilter===null||loadedFilter!==snapshot)return;
 exportButton.disabled=true;
 try{
  const response=await fetch(endpoint+'/export?'+snapshot,{credentials:'same-origin',cache:'no-store',headers:{Accept:'text/csv'}});
  if(!response.ok){const error=await response.json().catch(()=>null);throw Error(error?.message||'Readiness export failed.');}
  const blob=await response.blob();
  if(request!==sequence||loadedFilter!==snapshot||filters()!==snapshot)
   throw Error('Filters changed during export. Apply filters and retry.');
  const url=URL.createObjectURL(blob),link=document.createElement('a');
  link.href=url;link.download='vehicle-driver-readiness.csv';document.body.append(link);link.click();link.remove();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
 }catch(error){status.textContent=error.message;}
 finally{exportButton.disabled=loadedFilter===null||loadedFilter!==filters();}
});
(async()=>{
 try{
  const response=await fetch('/api/transport/route-setup/campuses',{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
  const body=await response.json().catch(()=>null);
  if(!response.ok||!body?.success||!Array.isArray(body.data))throw Error('Campus choices unavailable.');
  for(const item of body.data){const option=document.createElement('option');option.value=String(item.id);option.textContent=item.name;campus.append(option);}
 }catch(error){status.textContent=error.message;}
 await load();
})();
})();
