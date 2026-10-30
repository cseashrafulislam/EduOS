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
  const body = await guard.request('/api/transport/family-schedule?'+query,
   {headers:{Accept:'application/json'}}, async response => {
    const result = await response.json().catch(()=>null);
    if (!response.ok || !result?.success) throw Error(result?.message || 'Schedule unavailable.');
    return result;
   });
  if (!body?.success || !body.data) throw Error(body?.message || 'Schedule unavailable.');
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
  const blob = await guard.request('/api/transport/family-schedule/export?'+
   new URLSearchParams({operatingDate:requested}),{headers:{Accept:'text/csv'}},async response=>{
    if(!response.ok){const body=await response.json().catch(()=>null);throw Error(body?.message||'Export unavailable.');}
    return response.blob();
   });
  guard.assert();
  if (token !== serial || requested !== date.value || loadedDate !== requested)
   throw Error('Date changed; reload before exporting.');
  const url = URL.createObjectURL(blob), a = document.createElement('a');
  a.href = url; a.download = 'my-transport-schedule.csv'; document.body.append(a); a.click(); a.remove();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
 } catch(error) { status.textContent = error.message; }
 finally { exportButton.disabled = loadedDate !== date.value; }
});
/* Initial reads run only after the shared session guard is initialized. */

const attendanceDate = el('familyAttendanceDate'), attendanceRows = el('familyAttendanceRows');
const attendanceStatus = el('familyAttendanceStatus');
const attendancePrev = el('familyAttendancePrev'), attendanceNext = el('familyAttendanceNext');
let attendancePage = 1, attendancePages = 1, attendanceSerial = 0, attendanceLoadedDate = null;
const attendanceExport = el('familyAttendanceExport');
const historyDialog = el('familyAttendanceHistoryDialog');
const earliest = new Date(now.getFullYear(),now.getMonth(),now.getDate()-30);
attendanceDate.min = localDate(earliest); attendanceDate.max = localDate(now); attendanceDate.value = attendanceDate.max;
async function loadAttendance() {
 const requestDate = attendanceDate.value, token = ++attendanceSerial;
 attendanceLoadedDate = null; attendanceExport.disabled = true;
 attendanceRows.replaceChildren(); attendanceStatus.textContent = 'Loading / লোড হচ্ছে…';
 if (!requestDate || requestDate < attendanceDate.min || requestDate > attendanceDate.max) {
  attendanceStatus.textContent = 'Choose a date in the last 30 days / গত ৩০ দিনের তারিখ দিন'; return;
 }
 try {
  const query = new URLSearchParams({date:requestDate,page:String(attendancePage),pageSize:'20'});
  const body = await guard.request('/api/transport/family-attendance?'+query,
   {headers:{Accept:'application/json'}},async response=>{
    const result=await response.json().catch(()=>null);
    if(!response.ok||!result?.success) throw Error(result?.message||'Attendance unavailable.');
    return result;
   });
  if (!body?.success || !body.data) throw Error(body?.message||'Attendance unavailable.');
  if (token !== attendanceSerial || requestDate !== attendanceDate.value) return;
  for (const item of body.data.items || []) {
   const tr = document.createElement('tr');
   [item.studentCode,item.routeName,item.operatingDate,item.direction,
    item.attendanceState,item.lastEventAt ? new Date(item.lastEventAt+'Z').toLocaleString() : '—'
   ].forEach(x=>tr.append(cell(x)));
   const action = document.createElement('td'), history = document.createElement('button');
   history.type = 'button'; history.className = 'btn btn-outline-secondary btn-sm';
   history.textContent = 'History / ইতিহাস';
   history.addEventListener('click', async () => {
    history.disabled = true;
    try {
     const url = '/api/transport/family-attendance/' + encodeURIComponent(item.tripReference) +
      '/history?' + new URLSearchParams({studentCode:item.studentCode});
     const body = await guard.request(url,{headers:{Accept:'application/json'}},
      async response=>{
       const result=await response.json().catch(()=>null);
       if(!response.ok||!result?.success) throw Error(result?.message||'History unavailable.');
       return result;
      });
     if (!body?.success || !Array.isArray(body.data)) throw Error(body?.message||'History unavailable.');
     const list = el('familyAttendanceHistoryRows'); list.replaceChildren();
     el('familyAttendanceHistoryStudent').textContent = item.studentCode;
     for (const event of body.data) {
      const li = document.createElement('li');
      li.textContent = event.eventType + ' · ' + new Date(event.occurredAt + (event.occurredAt.endsWith('Z') ? '' : 'Z')).toLocaleString() +
       (event.isCorrection ? ' · Corrected / সংশোধিত' : '');
      list.append(li);
     }
     if (!body.data.length) { const li=document.createElement('li');li.textContent='No events / রেকর্ড নেই';list.append(li); }
     historyDialog.showModal();
    } catch (error) { if(guard.valid()) attendanceStatus.textContent = error.message; }
    finally { history.disabled = false; }
   });
   action.append(history); tr.append(action);
   attendanceRows.append(tr);
  }
  if (!body.data.items?.length) {
   const tr=document.createElement('tr'),td=cell('No dispatched trip / কোনো ডিসপ্যাচকৃত ট্রিপ নেই');
   td.colSpan=7;tr.append(td);attendanceRows.append(tr);
  }
  attendancePages=Math.max(1,Math.ceil((Number(body.data.totalCount)||0)/20));
  el('familyAttendancePage').textContent=attendancePage+' / '+attendancePages;
  attendancePrev.disabled=attendancePage<=1;attendanceNext.disabled=attendancePage>=attendancePages;
  attendanceLoadedDate = requestDate;
  attendanceExport.disabled = !body.data.items?.length;
  attendanceStatus.textContent='Recorded attendance only; not live tracking / রেকর্ডকৃত উপস্থিতি';
 } catch(e) { if(token===attendanceSerial) attendanceStatus.textContent=e.message; }
}
el('familyAttendanceFilters').addEventListener('submit',e=>{e.preventDefault();attendancePage=1;loadAttendance();});
attendanceDate.addEventListener('change',()=>{
 attendanceSerial++;attendancePage=1;attendanceLoadedDate=null;attendanceExport.disabled=true;
 attendanceRows.replaceChildren();
});
el('familyAttendanceHistoryClose').addEventListener('click',()=>historyDialog.close());
attendanceExport.addEventListener('click',async()=>{
 const requested=attendanceDate.value, token=attendanceSerial;
 if (attendanceLoadedDate!==requested) return;
 attendanceExport.disabled=true;
 try {
  const blob=await guard.request('/api/transport/family-attendance/export?'+
   new URLSearchParams({date:requested}),{headers:{Accept:'text/csv'}},async response=>{
    if(!response.ok){const body=await response.json().catch(()=>null);throw Error(body?.message||'Export unavailable.');}
    return response.blob();
   });
  guard.assert();
  if(token!==attendanceSerial||requested!==attendanceDate.value||attendanceLoadedDate!==requested)
   throw Error('Date changed; reload before exporting.');
  const url=URL.createObjectURL(blob),link=document.createElement('a');
  link.href=url;link.download='my-transport-attendance.csv';document.body.append(link);link.click();link.remove();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
 }catch(error){attendanceStatus.textContent=error.message;}
 finally{attendanceExport.disabled=attendanceLoadedDate!==attendanceDate.value;}
});
attendancePrev.addEventListener('click',()=>{if(attendancePage>1){attendancePage--;loadAttendance();}});
attendanceNext.addEventListener('click',()=>{if(attendancePage<attendancePages){attendancePage++;loadAttendance();}});
function scrub() {
 invalidate(); attendanceSerial++; attendanceLoadedDate=null;
 attendanceRows.replaceChildren(); attendanceExport.disabled=true;
 attendancePrev.disabled=true; attendanceNext.disabled=true;
 el('familyAttendancePage').textContent='';
 status.textContent=''; attendanceStatus.textContent='';
 el('familyAttendanceHistoryRows').replaceChildren();
 el('familyAttendanceHistoryStudent').textContent='';
 if(historyDialog.open) historyDialog.close();
}
const guard=window.EduOSTransportSession?.create(scrub);
if(guard?.valid()){load();loadAttendance();}else scrub();
})();
