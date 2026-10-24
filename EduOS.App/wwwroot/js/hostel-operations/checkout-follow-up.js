(() => {
'use strict';
const $ = id => document.getElementById(id);
if (!$('hostelCheckoutPanel')) return;
let page = 1, pages = 1, generation = 0, applied = null;
const status = value => { $('hostelCheckoutStatus').textContent = value; };
function filters(paged = true) {
    const q = new URLSearchParams(paged ? {page:String(page),pageSize:'25'} : {});
    const campus = $('hostelCheckoutCampus').value;
    const search = $('hostelCheckoutSearch').value.trim();
    if (campus) q.set('campusId',campus);
    if (search) q.set('search',search);
    q.set('daysAhead',$('hostelCheckoutDays').value);
    if ($('hostelCheckoutOverdue').checked) q.set('overdueOnly','true');
    return q.toString();
}
function invalidate() {
    generation++; applied = null;
    $('hostelCheckoutExport').disabled = true;
    $('hostelCheckoutPrev').disabled = true;
    $('hostelCheckoutNext').disabled = true;
    $('hostelCheckoutRows').replaceChildren();
    $('hostelCheckoutPage').textContent = '';
}
async function load() {
    invalidate();
    const token = generation, snapshot = filters(false);
    status('Loading checkout follow-ups…');
    try {
        const response = await fetch('/api/hostel/checkout-follow-ups?' + filters(), {
            credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
        const result = await response.json();
        if (token !== generation || snapshot !== filters(false)) return;
        if (!response.ok || !result.success) throw Error(result.message || 'Checkout report unavailable.');
        const data = result.data || {}, records = data.items || [];
        for (const item of records) {
            const tr = document.createElement('tr');
            for (const value of [item.campusName,item.hostelName,
                (item.roomNumber || '') + ' / ' + (item.bedNumber || ''),
                item.startDate,item.endDate,item.daysUntilCheckout,item.followUp]) {
                const td = document.createElement('td');
                td.textContent = String(value ?? '—'); tr.append(td);
            }
            $('hostelCheckoutRows').append(tr);
        }
        if (!records.length) {
            const tr = document.createElement('tr'), td = document.createElement('td');
            td.colSpan = 7; td.textContent = 'No checkout follow-ups match these filters.';
            tr.append(td); $('hostelCheckoutRows').append(tr);
        }
        pages = Math.max(1,Math.ceil((Number(data.totalCount) || 0)/25));
        $('hostelCheckoutPage').textContent = 'Page ' + page + ' of ' + pages +
            ' · ' + (Number(data.totalCount) || 0) + ' records';
        $('hostelCheckoutPrev').disabled = page <= 1;
        $('hostelCheckoutNext').disabled = page >= pages;
        applied = snapshot; $('hostelCheckoutExport').disabled = !records.length;
        status('Review checkout state with authorized staff; no automatic closure.');
    } catch (error) { if (token === generation) { invalidate(); status(error.message); } }
}
$('hostelCheckoutFilter').addEventListener('submit', e => { e.preventDefault(); page = 1; load(); });
for (const id of ['hostelCheckoutCampus','hostelCheckoutSearch','hostelCheckoutDays','hostelCheckoutOverdue']) {
    $(id).addEventListener('change', () => { page = 1; invalidate(); status('Filters changed; select Filter.'); });
}
$('hostelCheckoutSearch').addEventListener('input', () => { page = 1; invalidate(); });
$('hostelCheckoutPrev').addEventListener('click', () => { page = Math.max(1,page - 1); load(); });
$('hostelCheckoutNext').addEventListener('click', () => { page = Math.min(pages,page + 1); load(); });
$('hostelCheckoutExport').addEventListener('click', () => {
    const snapshot = filters(false);
    if (applied === null || snapshot !== applied) return;
    window.location.assign('/api/hostel/checkout-follow-ups/export?' + snapshot);
});
async function campuses() {
    try {
        const response = await fetch('/api/hostel/facility-campuses',{
            credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
        const result = await response.json();
        if (!response.ok || !result.success || !Array.isArray(result.data)) return;
        const select = $('hostelCheckoutCampus');
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
