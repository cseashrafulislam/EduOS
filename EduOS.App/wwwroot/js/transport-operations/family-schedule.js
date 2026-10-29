(() => {
'use strict';
const el = id => document.getElementById(id);
if (!el('transportFamilyPanel')) return;
const date = el('transportFamilyDate'), status = el('transportFamilyStatus');
const rows = el('transportFamilyRows'), exportButton = el('transportFamilyExport');
const prev = el('transportFamilyPrev'), next = el('transportFamilyNext');
const localDate = d => [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');
const now = new Date(), max = new Date(now.getFullYear(),now.getMonth(),now.getDate()+30);
date.min = localDate(now); date.max = localDate(max); date.value = date.min;
let page = 1, pages = 1, serial = 0, loadedDate = null;
function invalidate() {
 serial++; loadedDate = null; exportButton.disabled = true; prev.disabled = true; next.disabled = true;
 rows.replaceChildren(); el('transportFamilyPage').textContent = '';
}
function cell(value) {
 const td = document.createElement('td');
 td.textContent = value == null || value === '' ? '—' : String(value);
 return td;
}
async function load() {
 invalidate();
 if (!date.value || date.value < date.min || date.value > date.max) {
  status.textContent = 'Select a date within the next 30 days. / আগামী ৩০ দিনের তারিখ নির্বাচন করুন।'; return;
 }
 const requested = date.value, token = serial;
 status.textContent = 'Loading / লোড হচ্ছে…';
 try {
  const query = new URLSearchParams({operatingDate:requested,page:String(page),pageSize:'20'});
  const response = await fetch('/api/transport/family-schedule?'+query,{
   credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}
  });
  const body = await response.json().catch(()=>null);
  if (!response.ok || !body?.success || !body.data) throw Error(body?.message || 'Schedule unavailable.');
  if (token !== serial || requested !== date.value) return;
  const items = Array.isArray(body.data.items) ? body.data.items : [];
  for (const item of items) {
   const tr = document.createElement('tr');
   for (const value of [
    item.studentName+' ('+item.studentCode+')',item.campusName,item.routeName,
    item.vehicleNumber,item.pickupStop,item.dropStop,item.startDate,item.endDate||'—'
   ]) tr.append(cell(value));
   rows.append(tr);
  }
  if (!items.length) {
   const tr = document.createElement('tr'), td = cell('No assigned transport / পরিবহন বরাদ্দ নেই');
   td.colSpan = 8; tr.append(td); rows.append(tr);
  }
  pages = Math.max(1,Math.ceil((Number(body.data.totalCount)||0)/20));
  el('transportFamilyPage').textContent = 'Page '+page+' / '+pages;
  prev.disabled = page <= 1; next.disabled = page >= pages;
  loadedDate = requested; exportButton.disabled = !items.length;
  status.textContent = 'Assigned plan only / শুধুমাত্র নির্ধারিত সূচি';
 } catch (error) {
  if (token === serial) { invalidate(); status.textContent = error.message; }
 }
}
el('transportFamilyFilters').addEventListener('submit',e=>{e.preventDefault();page=1;load();});
date.addEventListener('change',()=>{page=1;invalidate();status.textContent='Apply to refresh / দেখতে ক্লিক করুন';});
prev.addEventListener('click',()=>{page=Math.max(1,page-1);load();});
next.addEventListener('click',()=>{page=Math.min(pages,page+1);load();});
exportButton.addEventListener('click',async()=>{
 const requested = date.value, token = serial;
 if (loadedDate !== requested) return;
 exportButton.disabled = true;
 try {
  const response = await fetch('/api/transport/family-schedule/export?'+new URLSearchParams({operatingDate:requested}),{
   credentials:'same-origin',cache:'no-store',headers:{Accept:'text/csv'}
  });
  if (!response.ok) {
   const body = await response.json().catch(()=>null);
   throw Error(body?.message||'Export unavailable.');
  }
  const blob = await response.blob();
  if (token !== serial || requested !== date.value || loadedDate !== requested)
   throw Error('Date changed; reload before exporting.');
  const url = URL.createObjectURL(blob), a = document.createElement('a');
  a.href = url; a.download = 'my-transport-schedule.csv'; document.body.append(a); a.click(); a.remove();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
 } catch(error) { status.textContent = error.message; }
 finally { exportButton.disabled = loadedDate !== date.value; }
});
load();
})();
