(() => {
'use strict';
const u=window.vehicleDriverUI;if(!u)return;
let page=1,pages=1,sequence=0,loadedFilter=null;
const exportButton=u.el('transportVehicleDriverExport');
const cell=value=>{const td=document.createElement('td');td.textContent=value==null?'—':String(value);return td;};
const filters=()=>{
 const q=new URLSearchParams();
 const campus=u.el('transportVehicleDriverCampus').value;
 const search=u.el('transportVehicleDriverSearch').value.trim();
 if(campus)q.set('campusId',campus);
 if(search)q.set('search',search);
 if(u.el('transportVehicleDriverScheduledOnly').checked)q.set('scheduledOnly','true');
 return q.toString();
};
function invalidate(){loadedFilter=null;exportButton.disabled=true;}
function renderRow(item){
 const tr=document.createElement('tr');
 [item.campusName,item.vehicleNumber,item.driverName,item.licenseExpiresOn,
  item.effectiveFrom,item.effectiveTo,item.isCancelled?'Cancelled':item.isScheduled?'Scheduled':item.isCurrent?'Current':'Ended'].forEach(x=>tr.append(cell(x)));
 const action=document.createElement('td');
 if(item.isScheduled){
  const button=document.createElement('button');button.type='button';
  button.className='btn btn-outline-danger btn-sm';button.textContent='Cancel reservation';
  button.onclick=async()=>{
   if(!confirm('Cancel this unused future driver reservation?'))return;
   button.disabled=true;
   try{await u.api(u.endpoint+'/'+item.id+'/cancel-upcoming','POST',{rowVersion:item.rowVersion});
    u.notify('Upcoming reservation cancelled.',true);await u.loadLinks();}
   catch(error){u.notify(error.message);}finally{button.disabled=false;}
  };
  action.append(button);
 }else if(item.isCurrent&&(!item.effectiveTo||item.effectiveTo>=u.today())){
  const date=document.createElement('input');date.type='date';date.className='form-control form-control-sm';
  date.setAttribute('aria-label','End date for '+item.vehicleNumber);
  date.min=item.effectiveFrom>u.today()?item.effectiveFrom:u.today();
  date.value=date.min;if(item.effectiveTo)date.max=item.effectiveTo;
  const button=document.createElement('button');button.type='button';
  button.className='btn btn-outline-danger btn-sm mt-1';button.textContent='Close';
  button.onclick=async()=>{
   if(!date.value||date.value<date.min||(date.max&&date.value>date.max))return u.notify('Invalid closure date.');
   if(!confirm('End this driver assignment on '+date.value+'?'))return;
   button.disabled=true;
   try{await u.api(u.endpoint+'/'+item.id+'/close','POST',{effectiveTo:date.value,rowVersion:item.rowVersion});
    u.notify('End date saved.',true);await u.loadLinks();}
   catch(error){u.notify(error.message);}finally{button.disabled=false;}
  };
  action.append(date,button);
 }else action.textContent='Closed';
 tr.append(action);return tr;
}
u.loadLinks=async()=>{
 invalidate();
 const current=++sequence, snapshot=filters();
 const q=new URLSearchParams(snapshot);q.set('page',String(page));q.set('pageSize','15');
 const data=await u.api(u.endpoint+'?'+q);
 if(current!==sequence||snapshot!==filters())return;
 const body=u.el('transportVehicleDriverRows');body.replaceChildren();
 (data.items||[]).forEach(item=>body.append(renderRow(item)));
 if(!data.items?.length){const tr=document.createElement('tr'),td=cell('No assignments');td.colSpan=8;tr.append(td);body.append(tr);}
 pages=Math.max(1,Number(data.totalPages)||1);
 u.el('transportVehicleDriverPageInfo').textContent='Page '+page+' of '+pages+' · '+data.totalCount+' records';
 u.el('transportVehicleDriverPrev').disabled=page<=1;
 u.el('transportVehicleDriverNext').disabled=page>=pages;
 loadedFilter=snapshot;exportButton.disabled=!data.items?.length||u.el('transportVehicleDriverScheduledOnly').checked;
};
u.el('transportVehicleDriverSearchForm').onsubmit=e=>{e.preventDefault();page=1;u.loadLinks().catch(x=>u.notify(x.message));};
u.el('transportVehicleDriverPrev').onclick=()=>{page=Math.max(1,page-1);u.loadLinks().catch(x=>u.notify(x.message));};
u.el('transportVehicleDriverNext').onclick=()=>{page=Math.min(pages,page+1);u.loadLinks().catch(x=>u.notify(x.message));};
u.el('transportVehicleDriverSearch').addEventListener('input',invalidate);
u.el('transportVehicleDriverCampus').addEventListener('change',invalidate);
u.el('transportVehicleDriverScheduledOnly').addEventListener('change',()=>{page=1;u.loadLinks().catch(x=>u.notify(x.message));});
exportButton.addEventListener('click',async()=>{
 const snapshot=filters(),request=sequence;
 if(loadedFilter===null||loadedFilter!==snapshot||u.el('transportVehicleDriverScheduledOnly').checked)return;
 exportButton.disabled=true;
 try{
  const response=await fetch(u.endpoint+'/export?'+snapshot,{credentials:'same-origin',cache:'no-store',headers:{Accept:'text/csv'}});
  if(!response.ok){
   const error=await response.json().catch(()=>null);
   throw Error(error?.message||'Vehicle/driver history export failed.');
  }
  const blob=await response.blob();
  if(request!==sequence||loadedFilter!==snapshot||filters()!==snapshot)
   throw Error('Filters changed during export. Apply filters and retry.');
  const url=URL.createObjectURL(blob),link=document.createElement('a');
  link.href=url;link.download='vehicle-driver-history.csv';document.body.append(link);link.click();link.remove();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
 }catch(error){u.notify(error.message);}
 finally{exportButton.disabled=loadedFilter===null||loadedFilter!==filters()||u.el('transportVehicleDriverScheduledOnly').checked;}
});
})();
