(() => {
'use strict';
const u=window.vehicleDriverUI;if(!u)return;
const el=u.el,panel=el('transportUpcomingDriverChangePanel');if(!panel)return;
let source=null,page=1,pages=1,lookup=0;
function dismiss(){
 source=null;++lookup;panel.hidden=true;
 el('transportUpcomingDriverChangeChoice').replaceChildren();
 el('transportUpcomingDriverChangeSave').disabled=true;
}
async function loadChoices(){
 if(!source)return;
 const current=++lookup,id=source.id,select=el('transportUpcomingDriverChangeChoice');
 select.replaceChildren();u.option(select,'','Loading eligible drivers…');
 el('transportUpcomingDriverChangeSave').disabled=true;
 try{
  const params=new URLSearchParams({page:String(page),pageSize:'20'});
  const search=el('transportUpcomingDriverChangeSearch').value.trim();
  if(search)params.set('search',search);
  const data=await u.api(u.endpoint+'/'+encodeURIComponent(id)+'/replacement-drivers?'+params);
  if(current!==lookup||source?.id!==id)return;
  select.replaceChildren();u.option(select,'','Choose a licensed, unassigned driver');
  for(const driver of data.items||[]){
   const choice=document.createElement('option');choice.value=String(driver.driverId);
   choice.textContent=driver.driverName+' · license expires '+driver.licenseExpiresOn;
   choice.dataset.rowVersion=driver.rowVersion;select.append(choice);
  }
  pages=Math.max(1,Number(data.totalPages)||1);
  el('transportUpcomingDriverChangePageInfo').textContent='Page '+page+' of '+pages+' · '+data.totalCount+' eligible drivers';
  el('transportUpcomingDriverChangePrev').disabled=page<=1;
  el('transportUpcomingDriverChangeNext').disabled=page>=pages;
  el('transportUpcomingDriverChangeSave').disabled=!(data.items||[]).length;
 }catch(error){if(current===lookup)u.notify(error.message);}
}
u.changeUpcomingDriver=async item=>{
 if(!item?.isScheduled||item.isCancelled||!item.rowVersion||item.effectiveFrom<=u.today())return;
 source=item;page=1;el('transportUpcomingDriverChangeSearch').value='';
 el('transportUpcomingDriverChangeContext').textContent=
  item.vehicleNumber+' · '+item.driverName+' · '+item.effectiveFrom+' through '+item.effectiveTo;
 panel.hidden=false;await loadChoices();
 panel.scrollIntoView({behavior:'smooth',block:'nearest'});
};
el('transportUpcomingDriverChangeSearchForm').onsubmit=e=>{e.preventDefault();page=1;loadChoices();};
el('transportUpcomingDriverChangePrev').onclick=()=>{page=Math.max(1,page-1);loadChoices();};
el('transportUpcomingDriverChangeNext').onclick=()=>{page=Math.min(pages,page+1);loadChoices();};
el('transportUpcomingDriverChangeCancel').onclick=dismiss;
el('transportUpcomingDriverChangeForm').onsubmit=async e=>{
 e.preventDefault();
 if(!source||source.effectiveFrom<=u.today())return u.notify('Only future reservations can change drivers.');
 const selected=el('transportUpcomingDriverChangeChoice').selectedOptions[0];
 const newDriverId=Number(selected?.value),driverRowVersion=selected?.dataset.rowVersion;
 if(!Number.isSafeInteger(newDriverId)||newDriverId<=0||!driverRowVersion)
  return u.notify('Select an eligible replacement driver.');
 if(!confirm('Change scheduled driver for '+source.vehicleNumber+'? Vehicle and dates remain unchanged.'))return;
 const button=el('transportUpcomingDriverChangeSave');button.disabled=true;
 try{
  await u.api(u.endpoint+'/'+encodeURIComponent(source.id)+'/change-upcoming-driver','POST',{
   newDriverId,assignmentRowVersion:source.rowVersion,driverRowVersion
  });
  dismiss();u.notify('Upcoming driver reservation updated.',true);await u.loadLinks();
 }catch(error){u.notify(error.message);}
 finally{button.disabled=!source;}
};
})();
