(() => {
'use strict';
const el=id=>document.getElementById(id);
if(!el('transportDriverCoverageWindowPanel'))return;
const endpoint='/api/transport/driver-coverage-window';
const from=el('transportDriverCoverageWindowFrom'),to=el('transportDriverCoverageWindowTo');
const campus=el('transportDriverCoverageWindowCampus'),search=el('transportDriverCoverageWindowSearch');
const attention=el('transportDriverCoverageWindowAttention'),status=el('transportDriverCoverageWindowStatus');
const rows=el('transportDriverCoverageWindowRows'),exportButton=el('transportDriverCoverageWindowExport');
const prev=el('transportDriverCoverageWindowPrev'),next=el('transportDriverCoverageWindowNext');
let page=1,pages=1,sequence=0,loadedFilter=null;
const iso=d=>[d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');
const today=new Date();from.value=iso(today);to.value=iso(new Date(today.getFullYear(),today.getMonth(),today.getDate()+13));from.min=iso(today);
const filters=()=>{
 const q=new URLSearchParams({fromDate:from.value,toDate:to.value,attentionOnly:attention.value});
 if(campus.value)q.set('campusId',campus.value);
 if(search.value.trim())q.set('search',search.value.trim());
 return q.toString();
};
const cell=value=>{const td=document.createElement('td');td.textContent=value==null?'—':String(value);return td;};
function invalidate(){sequence++;loadedFilter=null;exportButton.disabled=true;status.textContent='Filters changed. Apply to refresh.';prev.disabled=true;next.disabled=true;}
function validate(){
 const days=(Date.parse(to.value+'T00:00:00')-Date.parse(from.value+'T00:00:00'))/86400000;
 if(!from.value||!to.value||from.value<iso(new Date())||!Number.isFinite(days)||days<0||days>29)
  throw Error('Choose a future interval of 1–30 inclusive days.');
}
async function load(){
 try{validate();}catch(error){invalidate();status.textContent=error.message;return;}
 const request=++sequence,snapshot=filters(),q=new URLSearchParams(snapshot);
 loadedFilter=null;exportButton.disabled=true;q.set('page',String(page));q.set('pageSize','15');
 status.textContent='Checking dispatch coverage for every day…';
 try{
  const response=await fetch(endpoint+'?'+q,{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
  const body=await response.json().catch(()=>null);
  if(!response.ok||!body?.success||!body.data)throw Error(body?.message||'Dispatch window unavailable.');
  if(request!==sequence||snapshot!==filters())return;
  rows.replaceChildren();
  for(const item of body.data.items||[]){
   const tr=document.createElement('tr');
   [item.campusName,item.vehicleNumber,item.peakReservedRiders+' / '+item.capacity,item.peakDate,
    item.minimumFreeSeats,item.attentionDays,item.firstAttentionDate,item.firstIssue||'—']
    .forEach(value=>tr.append(cell(value)));
   if(item.needsAttention)tr.classList.add('table-warning');
   rows.append(tr);
  }
  if(!body.data.items?.length){const tr=document.createElement('tr'),td=cell('No matching vehicles.');td.colSpan=8;tr.append(td);rows.append(tr);}
  pages=Math.max(1,Number(body.data.totalPages)||1);
  el('transportDriverCoverageWindowPage').textContent='Page '+page+' of '+pages+' · '+body.data.totalCount+' vehicles';
  prev.disabled=page<=1;next.disabled=page>=pages;
  loadedFilter=snapshot;exportButton.disabled=!body.data.items?.length;
  status.textContent='Advisory only. Revalidate assignments and license status before dispatch.';
 }catch(error){if(request===sequence){loadedFilter=null;rows.replaceChildren();status.textContent=error.message;prev.disabled=true;next.disabled=true;}}
}
el('transportDriverCoverageWindowFilters').addEventListener('submit',event=>{event.preventDefault();page=1;load();});
for(const field of [from,to,campus,attention])field.addEventListener('change',invalidate);
search.addEventListener('input',invalidate);
prev.addEventListener('click',()=>{page=Math.max(1,page-1);load();});
next.addEventListener('click',()=>{page=Math.min(pages,page+1);load();});
exportButton.addEventListener('click',async()=>{
 const snapshot=filters(),request=sequence;
 if(loadedFilter===null||loadedFilter!==snapshot)return;
 exportButton.disabled=true;
 try{
  const response=await fetch(endpoint+'/export?'+snapshot,{credentials:'same-origin',cache:'no-store',headers:{Accept:'text/csv'}});
  if(!response.ok){const error=await response.json().catch(()=>null);throw Error(error?.message||'Export failed.');}
  const blob=await response.blob();
  if(request!==sequence||loadedFilter!==snapshot||filters()!==snapshot)throw Error('Filters changed during export.');
  const url=URL.createObjectURL(blob),link=document.createElement('a');
  link.href=url;link.download='driver-coverage-window.csv';document.body.append(link);link.click();link.remove();
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
