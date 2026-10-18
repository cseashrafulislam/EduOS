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
  if(!x.isCancelled && x.effectiveFrom>u.today() && Number(x.activeRiders)===0){
   const btn=document.createElement('button');btn.type='button';btn.textContent='Cancel reservation';
   btn.addEventListener('click',()=>u.cancelUpcomingLink(x));td.append(btn);
  }else if(!x.isCancelled && !x.effectiveTo){
   const btn=document.createElement('button');btn.type='button';btn.textContent='End';
   btn.addEventListener('click',()=>u.closeLink(x));
   td.append(btn);
  }
  tr.append(td);body.append(tr);
 }
};
})();