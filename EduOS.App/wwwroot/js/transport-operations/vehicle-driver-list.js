(() => {
const u=window.vehicleDriverUI;if(!u)return;
let page=1,pages=1,sequence=0;
const cell=value=>{const td=document.createElement('td');td.textContent=value==null?'—':String(value);return td;};
function renderRow(item){
 const tr=document.createElement('tr');
 [item.campusName,item.vehicleNumber,item.driverName,item.licenseExpiresOn,
  item.effectiveFrom,item.effectiveTo,item.isCurrent?'Current':'Not current'].forEach(x=>tr.append(cell(x)));
 const action=document.createElement('td');
 if(!item.effectiveTo||item.effectiveTo>=u.today()){
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
 const current=++sequence,q=new URLSearchParams({page:String(page),pageSize:'15'});
 const campus=u.el('transportVehicleDriverCampus').value;
 const search=u.el('transportVehicleDriverSearch').value.trim();
 if(campus)q.set('campusId',campus);if(search)q.set('search',search);
 const data=await u.api(u.endpoint+'?'+q);
 if(current!==sequence)return;
 const body=u.el('transportVehicleDriverRows');body.replaceChildren();
 (data.items||[]).forEach(item=>body.append(renderRow(item)));
 if(!data.items?.length){const tr=document.createElement('tr'),td=cell('No assignments');td.colSpan=8;tr.append(td);body.append(tr);}
 pages=Math.max(1,Number(data.totalPages)||1);
 u.el('transportVehicleDriverPageInfo').textContent='Page '+page+' of '+pages+' · '+data.totalCount+' records';
 u.el('transportVehicleDriverPrev').disabled=page<=1;
 u.el('transportVehicleDriverNext').disabled=page>=pages;
};
u.el('transportVehicleDriverSearchForm').onsubmit=e=>{e.preventDefault();page=1;u.loadLinks().catch(x=>u.notify(x.message));};
u.el('transportVehicleDriverPrev').onclick=()=>{page=Math.max(1,page-1);u.loadLinks().catch(x=>u.notify(x.message));};
u.el('transportVehicleDriverNext').onclick=()=>{page=Math.min(pages,page+1);u.loadLinks().catch(x=>u.notify(x.message));};
})();
