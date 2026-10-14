(() => {
const u=window.transportLink;if(!u)return;
u.closeLink=async item=>{
 const end=window.prompt('Last effective date (YYYY-MM-DD)',u.today());
 if(!end)return;
 if(!/^\d{4}-\d{2}-\d{2}$/.test(end)||end<u.today()||end<item.effectiveFrom){
  u.notify('End date must be today or later and after the start.');return;
 }
 try{
  await u.api('/api/transport/route-vehicle-links/'+encodeURIComponent(item.id)+'/close','POST',{effectiveTo:end,rowVersion:item.rowVersion});
  u.notify('Assignment end date saved.',true);await u.load();
 }catch(error){u.notify(error.message);}
};
})();