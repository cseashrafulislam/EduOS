(() => {
    'use strict';
    const root = document.getElementById('procurementWorkspace');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
    const en = { title:'Purchase requisitions', export:'Excel / CSV', new:'New requisition',
        help:'Submit a draft for independent approval. Approval does not create stock or financial postings.',
        campus:'Campus ID', status:'Status', all:'All', search:'Search', filter:'Filter', number:'Number',
        purpose:'Purpose', total:'Estimated total', created:'Created', actions:'Actions', previous:'Previous',
        next:'Next', details:'Requisition details', items:'Requested items', add:'Add line', close:'Close',
        save:'Save draft', choose:'Choose inventory item', select:'Select', edit:'Edit', submit:'Submit',
        approve:'Approve', reject:'Reject', withdraw:'Withdraw for revision', cancel:'Cancel', quantity:'Quantity', price:'Estimated price',
        remove:'Remove', empty:'No requisitions found.', reason:'Decision reason', page:'Page', of:'of',
        error:'Request failed. Reload and try again.', saved:'Requisition saved.', confirm:'Confirm action?' };
    const bd = { title:'ক্রয় চাহিদাপত্র', export:'এক্সেল / CSV', new:'নতুন চাহিদাপত্র',
        help:'খসড়া স্বাধীন অনুমোদনের জন্য জমা দিন। অনুমোদন নিজে থেকে স্টক বা হিসাব পোস্ট করে না।',
        campus:'ক্যাম্পাস আইডি', status:'অবস্থা', all:'সব', search:'খুঁজুন', filter:'ফিল্টার', number:'নম্বর',
        purpose:'উদ্দেশ্য', total:'আনুমানিক মোট', created:'তৈরির তারিখ', actions:'কার্যক্রম',
        previous:'আগের', next:'পরের', details:'চাহিদাপত্রের বিবরণ', items:'প্রয়োজনীয় পণ্য',
        add:'লাইন যোগ', close:'বন্ধ', save:'খসড়া সংরক্ষণ', choose:'পণ্য নির্বাচন',
        select:'নির্বাচন', edit:'সম্পাদনা', submit:'জমা', approve:'অনুমোদন',
        reject:'প্রত্যাখ্যান', withdraw:'সংশোধনের জন্য ফেরত', cancel:'বাতিল', quantity:'পরিমাণ', price:'আনুমানিক মূল্য',
        remove:'মুছুন', empty:'কোনো চাহিদাপত্র পাওয়া যায়নি।', reason:'সিদ্ধান্তের কারণ',
        page:'পৃষ্ঠা', of:'এর', error:'অনুরোধ ব্যর্থ। পুনরায় চেষ্টা করুন।',
        saved:'চাহিদাপত্র সংরক্ষিত।', confirm:'নিশ্চিত করবেন?' };
    const t = key => (bn ? bd : en)[key] || key;
    root.querySelectorAll('[data-i18n]').forEach(el => { el.textContent = t(el.dataset.i18n); });
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    const base = '/api/procurement/requisitions';
    const canMake = root.dataset.maker === 'true';
    const canCheck = root.dataset.checker === 'true';
    const currentUser = Number(root.dataset.userId);
    let page = 1, totalPages = 1, rows = [], selectedItemLine = null, epoch = 0, busy = false;
    const dialog = $('prDialog'), picker = $('prItemDialog');
    const notice = (message, ok = false) => {
        const box = $('prAlert');
        box.className = 'alert alert-' + (ok ? 'success' : 'danger');
        box.textContent = message;
    };
    async function api(path, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        if (method !== 'GET' && token) headers.RequestVerificationToken = token;
        const response = await fetch(path.startsWith('/api/') ? path : base + path, {
            method, credentials:'same-origin', cache:'no-store', headers,
            ...(body === undefined ? {} : { body:JSON.stringify(body) })
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok || !payload?.success) throw new Error(payload?.message || t('error'));
        return payload.data;
    }
    function cell(value) {
        const td = document.createElement('td');
        td.textContent = value == null || value === '' ? '—' : String(value);
        return td;
    }
    function button(text, handler, kind = 'outline-primary') {
        const b = document.createElement('button');
        b.type = 'button'; b.className = 'btn btn-' + kind + ' btn-sm me-1 mb-1';
        b.textContent = t(text); b.addEventListener('click', handler); return b;
    }
    function date(value) { return value ? new Date(value).toLocaleDateString(bn ? 'bn-BD' : 'en-BD') : '—'; }
    function render() {
        const body = $('prRows'); body.replaceChildren();
        if (!rows.length) {
            const tr = document.createElement('tr'), td = cell(t('empty'));
            td.colSpan = 6; tr.append(td); body.append(tr);
        }
        for (const r of rows) {
            const tr = document.createElement('tr');
            [r.requestNumber, r.purpose, r.stateCode,
             Number(r.estimatedTotal).toLocaleString(bn ? 'bn-BD' : 'en-BD'),
             date(r.createdAt)].forEach(v => tr.append(cell(v)));
            const actions = document.createElement('td');
            actions.append(button('details', () => openDetail(r.id)));
            const owner = r.createdBy === currentUser;
            if (r.stateCode === 'Draft' && canMake && owner) {
                actions.append(button('edit', () => openDetail(r.id)));
                actions.append(button('submit', () => transition(r, 'submit')));
            }
            if (r.stateCode === 'Submitted' && canMake && owner)
                actions.append(button('withdraw', () => transition(r, 'withdraw')));
            if (r.stateCode === 'Submitted' && canCheck && !owner) {
                actions.append(button('approve', () => transition(r, 'approve'), 'outline-success'));
                actions.append(button('reject', () => transition(r, 'reject'), 'outline-danger'));
            }
            if (['Draft', 'Submitted'].includes(r.stateCode) && canMake && owner)
                actions.append(button('cancel', () => transition(r, 'cancel'), 'outline-danger'));
            tr.append(actions); body.append(tr);
        }
        $('prPage').textContent = t('page') + ' ' + page + ' ' + t('of') + ' ' + totalPages;
        $('prPrev').disabled = page <= 1; $('prNext').disabled = page >= totalPages;
    }
    async function load() {
        const requestEpoch = ++epoch;
        const query = new URLSearchParams({ page:String(page), pageSize:'25',
            search:$('prSearch').value.trim(), state:$('prState').value });
        if ($('prCampus').value) query.set('campusId', $('prCampus').value);
        try {
            const data = await api('?' + query);
            if (requestEpoch !== epoch) return;
            rows = data.items || []; totalPages = Math.max(1, data.totalPages || 1); render();
        } catch (err) { if (requestEpoch === epoch) notice(err.message); }
    }
    function addLine(data = {}) {
        const wrapper = document.createElement('div');
        wrapper.className = 'row g-2 mb-2 align-items-end pr-line';
        wrapper.innerHTML = '<div class="col-12 col-md-5"><label class="form-label"></label><div class="input-group"><input class="form-control pr-item" type="text" readonly required /><button class="btn btn-outline-primary pr-pick" type="button">…</button></div></div>' +
            '<div class="col-5 col-md-2"><label class="form-label"></label><input type="number" min="0.000001" step="0.000001" class="form-control pr-qty" required /></div>' +
            '<div class="col-5 col-md-3"><label class="form-label"></label><input type="number" min="0" step="0.0001" class="form-control pr-price" required /></div>' +
            '<div class="col-2"><button class="btn btn-outline-danger pr-remove" type="button"></button></div>';
        wrapper.querySelector('.col-md-5 label').textContent = t('items');
        wrapper.querySelector('.col-md-2 label').textContent = t('quantity');
        wrapper.querySelector('.col-md-3 label').textContent = t('price');
        wrapper.querySelector('.pr-remove').textContent = '×';
        wrapper.querySelector('.pr-item').value = data.inventoryItemId || '';
        wrapper.querySelector('.pr-qty').value = data.quantity ?? 1;
        wrapper.querySelector('.pr-price').value = data.estimatedUnitPrice ?? 0;
        wrapper.querySelector('.pr-pick').addEventListener('click', () => {
            selectedItemLine = wrapper.querySelector('.pr-item');
            $('prItemTerm').value = ''; picker.hidden = false; searchItems();
        });
        wrapper.querySelector('.pr-remove').addEventListener('click', () => wrapper.remove());
        $('prLines').append(wrapper);
    }
    function setEditable(enabled) {
        $('prForm').querySelectorAll('input,button').forEach(el => {
            if (!['prClose', 'prDismiss'].includes(el.id)) el.disabled = !enabled;
        });
        $('prSave').hidden = !enabled; $('prAddLine').hidden = !enabled;
    }
    function newDraft() {
        $('prForm').reset(); $('prId').value = ''; $('prVersion').value = '';
        $('prRequestId').value = crypto.randomUUID();
        $('prFormCampus').value = $('prCampus').value;
        $('prLines').replaceChildren(); addLine(); picker.hidden = true; setEditable(true); dialog.showModal();
    }
    async function openDetail(id) {
        try {
            const r = await api('/' + encodeURIComponent(id));
            $('prId').value = r.id; $('prVersion').value = r.rowVersion;
            // The original request key is not returned: edits are intentionally unavailable
            // until the same key is exposed by the authorized read contract.
            $('prRequestId').value = r.clientRequestId || '';
            $('prFormCampus').value = r.campusId; $('prPurpose').value = r.purpose;
            $('prLines').replaceChildren(); (r.lines || []).forEach(addLine);
            picker.hidden = true; setEditable(r.stateCode === 'Draft' && canMake && r.createdBy === currentUser &&
                !!r.clientRequestId);
            dialog.showModal();
        } catch (err) { notice(err.message); }
    }
    async function save(event) {
        event.preventDefault();
        if (busy) return;
        const lines = Array.from($('prLines').querySelectorAll('.pr-line')).map(row => ({
            inventoryItemId:Number(row.querySelector('.pr-item').value),
            quantity:Number(row.querySelector('.pr-qty').value),
            estimatedUnitPrice:Number(row.querySelector('.pr-price').value)
        }));
        const id = $('prId').value;
        const body = { clientRequestId:$('prRequestId').value, campusId:Number($('prFormCampus').value),
            purpose:$('prPurpose').value.trim(), rowVersion:$('prVersion').value || null, lines };
        busy = true; $('prSave').disabled = true;
        try {
            await api(id ? '/' + id : '', id ? 'PUT' : 'POST', body);
            dialog.close(); notice(t('saved'), true); await load();
        } catch (err) { notice(err.message); }
        finally { busy = false; $('prSave').disabled = false; }
    }
    async function transition(row, action) {
        if (busy) return;
        let reason = '';
        if (action === 'reject' || action === 'withdraw') {
            reason = window.prompt(t('reason')) || '';
            if (!reason.trim()) return;
        } else if (!window.confirm(t('confirm'))) return;
        busy = true;
        try {
            const current = await api('/' + encodeURIComponent(row.id));
            await api('/' + encodeURIComponent(row.id) + '/' + action, 'POST',
                { rowVersion:current.rowVersion, reason });
            await load();
        } catch (err) { notice(err.message); }
        finally { busy = false; }
    }
    async function searchItems() {
        const query = new URLSearchParams({ page:'1', pageSize:'25', search:$('prItemTerm').value.trim() });
        try {
            const data = await api('/api/inventory/catalog/items?' + query);
            const list = $('prItemResults'); list.replaceChildren();
            for (const item of data.items || []) {
                if (!item.isActive || !item.isStockTracked) continue;
                list.append(button(item.code + ' — ' + item.name, () => {
                    if (selectedItemLine) selectedItemLine.value = item.id;
                    picker.hidden = true;
                }));
            }
        } catch (err) { notice(err.message); }
    }
    $('prHelp').addEventListener('click', () => {
        const hidden = $('prHelpText').classList.toggle('d-none');
        $('prHelp').setAttribute('aria-expanded', String(!hidden));
    });
    $('prFilters').addEventListener('submit', e => { e.preventDefault(); page = 1; load(); });
    $('prPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
    $('prNext').addEventListener('click', () => { if (page < totalPages) { page++; load(); } });
    $('prExport').addEventListener('click', () => {
        const q = new URLSearchParams({ state:$('prState').value, search:$('prSearch').value.trim() });
        if ($('prCampus').value) q.set('campusId', $('prCampus').value);
        window.location.assign(base + '/export?' + q);
    });
    $('prNew')?.addEventListener('click', newDraft);
    $('prAddLine').addEventListener('click', () => addLine());
    $('prForm').addEventListener('submit', save);
    $('prClose').addEventListener('click', () => dialog.close());
    $('prDismiss').addEventListener('click', () => dialog.close());
    $('prItemSearchButton').addEventListener('click', searchItems);
    $('prItemClose').addEventListener('click', () => { picker.hidden = true; });
    window.addEventListener('pageshow', e => { if (e.persisted) { epoch++; rows = []; render(); load(); } });
    load();
})();
