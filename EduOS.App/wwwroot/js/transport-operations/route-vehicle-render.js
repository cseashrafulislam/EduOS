(() => {
const u=window.transportLink;if(!u)return;
u.renderLinks=items=>{
 const body=u.el('transportLinkRows');body.replaceChildren();
 for(const x of items||[]){
  const tr=document.createElement('tr');
  for(const value of [x.campusName,x.routeName,x.vehicleNumber,x.capacity,x.activeRiders,x.effectiveFrom,x.effectiveTo||'Open',x.isCurrent?'Current':'Not current']){
   const td=document.createElement('td');td.textContent=String(value);tr.append(td);
  }
  const td=document.createElement('td');
  if(!x.effectiveTo){
   const btn=document.createElement('button');btn.type='button';btn.textContent='End';
   btn.addEventListener('click',()=>u.closeLink(x));
   td.append(btn);
  }
  tr.append(td);body.append(tr);
 }
};
})();