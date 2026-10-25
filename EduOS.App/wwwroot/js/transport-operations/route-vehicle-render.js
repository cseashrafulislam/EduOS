(() => {
const u=window.transportLink;if(!u)return;
u.renderLinks=items=>{
 const body=u.el('transportLinkRows');body.replaceChildren();
 for(const x of items||[]){
  const tr=document.createElement('tr');
  for(const value of [x.campusName,x.routeName,x.vehicleNumber,x.capacity,x.occupiedToday,x.activeRiders,x.effectiveFrom,x.effectiveTo||'Open',x.isCancelled?'Cancelled':x.isCurrent?'Current':x.effectiveFrom>u.today()?'Scheduled':'Ended']){
   const td=document.createElement('td');td.textContent=String(value);tr.append(td);
  }
  const td=document.createElement('td');
  if(!x.isCancelled && x.isCurrent && x.effectiveFrom>u.today()){
   const reschedule=document.createElement('button');reschedule.type='button';
   reschedule.textContent='Reschedule start';reschedule.className='btn btn-outline-primary btn-sm me-1';
   reschedule.addEventListener('click',()=>u.rescheduleUpcomingLink(x));td.append(reschedule);
  }
  if(!x.isCancelled && x.effectiveFrom>u.today() && Number(x.activeRiders)===0){
   const change=document.createElement('button');change.type='button';
   change.textContent='Change scheduled vehicle';change.className='btn btn-outline-primary btn-sm me-1';
   change.addEventListener('click',()=>u.changeUpcomingRouteVehicle(x));td.append(change);
  }
  if(!x.isCancelled && x.effectiveFrom>u.today() && Number(x.activeRiders)===0){
   const btn=document.createElement('button');btn.type='button';btn.textContent='Cancel reservation';
   btn.addEventListener('click',()=>u.cancelUpcomingLink(x));td.append(btn);
  }else if(!x.isCancelled && x.isCurrent && x.effectiveFrom<=u.today() && x.effectiveTo){
   const btn=document.createElement('button');btn.type='button';btn.textContent='Extend';
   btn.addEventListener('click',()=>u.extendLink(x));td.append(btn);
  }else if(!x.isCancelled && !x.effectiveTo){
   const btn=document.createElement('button');btn.type='button';btn.textContent='End';
   btn.addEventListener('click',()=>u.closeLink(x));
   td.append(btn);
  }
  tr.append(td);body.append(tr);
 }
};
})();