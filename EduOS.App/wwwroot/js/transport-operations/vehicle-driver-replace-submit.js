(() => {
const u=window.vehicleDriverUI;if(!u)return;
u.el('transportVehicleDriverReplaceForm').onsubmit=e=>{
 e.preventDefault();
 const id=u.el('transportVehicleDriverReplaceSource').value;
 const item=u.replacementRows.find(x=>String(x.id)===id);
 if(item)u.replaceDriver(item);else u.notify('Select an operating vehicle assignment.');
};
})();
