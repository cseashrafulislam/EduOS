(() => {
'use strict';
const el=id=>document.getElementById(id);
if(!el('transportDriverCompliancePanel'))return;
const endpoint='/api/transport/driver-license-compliance';
const search=el('transportDriverComplianceSearch'),state=el('transportDriverComplianceState');
const days=el('transportDriverComplianceDays'),status=el('transportDriverComplianceStatus');
const rows=el('transportDriverComplianceRows'),exportButton=el('transportDriverComplianceExport');
const previous=el('transportDriverCompliancePrev'),next=el('transportDriverComplianceNext');
let page=1,pages=1,sequence=0,loadedFilter=null;
const filters=()=>{
 const q=new URLSearchParams({state:state.value,withinDays:days.value});
 const term=search.value.trim();if(term)q.set('search',term);return q.toString();
};
function invalidate(){
 sequence++;loadedFilter=null;exportButton.disabled=true;
 status.textContent='Filters changed; apply to refresh the report.';
}
const cell=value=>{const td=document.createElement('td');td.textContent=value==null?'—':String(value);return td;};
async function load(){
 loadedFilter=null;exportButton.disabled=true;
 const request=++sequence,snapshot=filters(),q=new URLSearchParams(snapshot);
 q.set('page',String(page));q.set('pageSize','15');
 status.textContent='Loading license compliance…';
 try{
  const response=await fetch(endpoint+'?'+q,{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
  const body=await response.json().catch(()=>null);
  if(!response.ok||!body?.success||!body.data)throw Error(body?.message||'Compliance report unavailable.');
  if(request!==sequence||snapshot!==filters())return;
  const data=body.data;rows.replaceChildren();
  for(const item of data.items||[]){
   const tr=document.createElement('tr');
   [item.driverName,item.licenseExpiresOn,item.daysUntilExpiry,item.complianceState,
    item.hasCurrentVehicle?'Yes':'No'].forEach(value=>tr.append(cell(value)));
   rows.append(tr);
  }
  if(!data.items?.length){const tr=document.createElement('tr'),td=cell('No matching active drivers');td.colSpan=5;tr.append(td);rows.append(tr);}
  pages=Math.max(1,Number(data.totalPages)||1);
  el('transportDriverCompliancePage').textContent='Page '+page+' of '+pages+' · '+data.totalCount+' drivers';
  previous.disabled=page<=1;next.disabled=page>=pages;
  loadedFilter=snapshot;exportButton.disabled=!data.items?.length;
  status.textContent='Current license compliance; refresh before taking action.';
 }catch(error){if(request===sequence){rows.replaceChildren();status.textContent=error.message;previous.disabled=true;next.disabled=true;}}
}
el('transportDriverComplianceFilters').addEventListener('submit',event=>{event.preventDefault();page=1;load();});
search.addEventListener('input',invalidate);
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
  if(!response.ok){const error=await response.json().catch(()=>null);throw Error(error?.message||'Compliance export failed.');}
  const blob=await response.blob();
  if(request!==sequence||loadedFilter!==snapshot||filters()!==snapshot)
   throw Error('Filters changed during export. Apply filters and retry.');
  const url=URL.createObjectURL(blob),link=document.createElement('a');
  link.href=url;link.download='driver-license-compliance.csv';document.body.append(link);link.click();link.remove();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
 }catch(error){status.textContent=error.message;}
 finally{exportButton.disabled=loadedFilter===null||loadedFilter!==filters();}
});
load();
})();
