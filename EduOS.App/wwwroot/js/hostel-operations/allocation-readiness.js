(() => {
'use strict';
const $ = id => document.getElementById(id);
if (!$('hostelAllocationReadinessPanel')) return;
let page = 1, pages = 1, generation = 0, applied = null;
const status = message => { $('hostelReadinessStatus').textContent = message; };
function filters(paged = true) {
    const q = new URLSearchParams(paged ? {page:String(page),pageSize:'25'} : {});
    const campus = $('hostelReadinessCampus').value;
    const search = $('hostelReadinessSearch').value.trim();
    if (campus) q.set('campusId',campus);
    if (search) q.set('search',search);
    if ($('hostelReadinessAttention').checked) q.set('attentionOnly','true');
    return q.toString();
}
function invalidate() {
    generation++; applied = null;
    $('hostelReadinessExport').disabled = true;
    $('hostelReadinessPrev').disabled = true;
    $('hostelReadinessNext').disabled = true;
    $('hostelReadinessRows').replaceChildren();
    $('hostelReadinessPage').textContent = '';
}
async function load() {
    invalidate();
    const token = generation, snapshot = filters(false);
    status('Loading allocation readiness…');
    try {
        const response = await fetch('/api/hostel/allocation-readiness?' + filters(), {
            credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
        const result = await response.json();
        if (token !== generation || snapshot !== filters(false)) return;
        if (!response.ok || !result.success) throw Error(result.message || 'Readiness unavailable.');
        const data = result.data || {}, records = data.items || [];
        for (const item of records) {
            const row = document.createElement('tr');
            for (const value of [item.campusName,item.hostelName,
                (item.roomNumber || '') + ' / ' + (item.bedNumber || ''),
                item.startDate,item.readiness]) {
                const cell = document.createElement('td');
                cell.textContent = String(value ?? '—'); row.append(cell);
            }
            $('hostelReadinessRows').append(row);
        }
        if (!records.length) {
            const row = document.createElement('tr'), cell = document.createElement('td');
            cell.colSpan = 5; cell.textContent = 'No matching arrivals or exceptions.';
            row.append(cell); $('hostelReadinessRows').append(row);
        }
        pages = Math.max(1,Math.ceil((Number(data.totalCount) || 0)/25));
        $('hostelReadinessPage').textContent = 'Page ' + page + ' of ' + pages +
            ' · ' + (Number(data.totalCount) || 0) + ' records';
        $('hostelReadinessPrev').disabled = page <= 1;
        $('hostelReadinessNext').disabled = page >= pages;
        applied = snapshot; $('hostelReadinessExport').disabled = !records.length;
        status('Campus-authorized operational data; no student identity or financial details.');
    } catch (error) { if (token === generation) { invalidate(); status(error.message); } }
}
$('hostelReadinessFilter').addEventListener('submit', e => { e.preventDefault(); page = 1; load(); });
for (const id of ['hostelReadinessCampus','hostelReadinessSearch','hostelReadinessAttention']) {
    $(id).addEventListener('change', () => { page = 1; invalidate(); status('Filters changed; select Filter.'); });
}
$('hostelReadinessSearch').addEventListener('input', () => { page = 1; invalidate(); });
$('hostelReadinessPrev').addEventListener('click', () => { page = Math.max(1,page - 1); load(); });
$('hostelReadinessNext').addEventListener('click', () => { page = Math.min(pages,page + 1); load(); });
$('hostelReadinessExport').addEventListener('click', () => {
    const snapshot = filters(false);
    if (applied === null || snapshot !== applied) return;
    window.location.assign('/api/hostel/allocation-readiness/export?' + snapshot);
});
async function campuses() {
    try {
        const response = await fetch('/api/hostel/facility-campuses',{
            credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
        const result = await response.json();
        if (!response.ok || !result.success || !Array.isArray(result.data)) return;
        const select = $('hostelReadinessCampus');
        select.replaceChildren(new Option('All permitted campuses',''));
        for (const campus of result.data) {
            const id = Number(campus.id);
            if (Number.isSafeInteger(id) && id > 0)
                select.add(new Option(String(campus.name || 'Campus'),String(id)));
        }
    } catch { status('Campus options unavailable.'); }
}
load(); campuses();
})();
