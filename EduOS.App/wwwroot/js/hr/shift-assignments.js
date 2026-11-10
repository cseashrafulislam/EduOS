(() => {
    'use strict';
    const root = document.getElementById('hrShiftWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    const state = { employee: null, page: 1, pages: 1, requestKey: null, signature: null,
        busy: false, accessRevoked: false, searchSeq: 0, loadSeq: 0, lastPostedFingerprint: null };
    const form=el('hrShiftForm'),guard=window.EduOSFormGuard?.create(form);
    function localDate(){const d=new Date();return [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');}
    function confirmDiscard(){return !guard?.isDirty()||guard.confirmDiscard();}
    function resetDraft(){form.reset();el('hrShiftFrom').value=localDate();
        state.requestKey=null;state.signature=null;state.lastPostedFingerprint=null;guard?.markClean();}
    const cell = value => { const td = document.createElement('td'); td.textContent = value == null || value === '' ? '—' : String(value); return td; };
    const date = value => value ? String(value).slice(0, 10) : '';
    function notice(message, success = false) {
        const box = el('hrShiftAlert');
        box.className = 'alert alert-' + (success ? 'success' : 'danger');
        box.textContent = message;
        box.focus();
    }
    async function api(path, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (method !== 'GET') {
            if (!token) throw new Error('Missing antiforgery token. Reload this page.');
            headers.RequestVerificationToken = token;
        }
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        const response = await fetch(path, { method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success){const error=new Error(result?.message || 'Request failed.');
            error.statusCode=response.status;throw error;}
        return result.data;
    }
    function handleError(error){
        if([401,403].includes(error.statusCode)){
            state.accessRevoked=true;++state.searchSeq;++state.loadSeq;state.employee=null;
            el('hrShiftRows').replaceChildren();el('hrShiftEmployee').replaceChildren(option('','Access unavailable'));
            el('hrShiftCode').replaceChildren(option('','Access unavailable'));
            form.reset();guard?.markClean();root.inert=true;
        }
        notice(error.message);
    }
    function option(value, label) {
        const o = document.createElement('option'); o.value = value; o.textContent = label; return o;
    }
    async function findEmployees() {
        if(state.busy||state.accessRevoked||!confirmDiscard())return;
        const search = el('hrShiftSearch').value.trim();
        if (search.length < 2 || search.length > 100) return notice('Enter 2–100 characters to search employees.');
        const seq=++state.searchSeq;
        try {
            const data = await api('/api/hr/employees?search=' + encodeURIComponent(search) + '&page=1&pageSize=50');
            if(seq!==state.searchSeq||search!==el('hrShiftSearch').value.trim()||state.busy||!confirmDiscard())return;
            const select = el('hrShiftEmployee');
            select.replaceChildren(option('', 'Choose employee'));
            for (const employee of data?.items || []) select.append(option(employee.reference, employee.employeeCode + ' — ' + employee.fullName));
            state.employee = null;state.page=1;state.pages=1;++state.loadSeq;resetDraft();
            el('hrShiftRows').replaceChildren();
            el('hrShiftPage').textContent = 'Select employee.';
            if (!(data?.items || []).length) notice('No matching employees.');
        } catch (error) {if(seq===state.searchSeq)handleError(error);}
    }
    async function loadShifts() {
        const rows = await api('/api/hr/setup/shifts');
        const select = el('hrShiftCode');
        select.replaceChildren(option('', 'Choose active shift'));
        for (const shift of rows || []) if (shift.isActive) select.append(option(shift.id, shift.name + ' (' + String(shift.startTime).slice(0, 5) + '–' + String(shift.endTime).slice(0, 5) + ')'));
    }
    async function loadHistory() {
        const reference = state.employee,seq=++state.loadSeq,page=state.page;
        const body = el('hrShiftRows');
        body.replaceChildren();
        if (!reference) return;
        el('hrShiftPage').textContent='Loading shift assignments…';
        el('hrShiftPrev').disabled=true;el('hrShiftNext').disabled=true;
        try {
            const data = await api('/api/hr/employees/' + encodeURIComponent(reference) + '/shift-assignments?page=' + page + '&pageSize=20');
            if(seq!==state.loadSeq||reference!==state.employee||page!==state.page)return;
            const rows = data?.items || [];
            for (const row of rows) {
                const tr = document.createElement('tr');
                [row.workShiftName, date(row.effectiveFrom), date(row.effectiveTo) || 'Open', row.isCurrent ? 'Current' : 'Historical'].forEach(value => tr.append(cell(value)));
                body.append(tr);
            }
            if (!rows.length) { const tr = document.createElement('tr'), td = cell('No shift assignments.'); td.colSpan = 4; tr.append(td); body.append(tr); }
            state.pages = Math.max(1, Math.ceil(Number(data?.totalCount || 0) / 20));
            state.page = Number(data?.page) || state.page;
            el('hrShiftPage').textContent = 'Page ' + state.page + ' of ' + state.pages + ' · ' + Number(data?.totalCount || 0) + ' records';
            el('hrShiftPrev').disabled = state.page <= 1;
            el('hrShiftNext').disabled = state.page >= state.pages;
        } catch (error) {if(seq===state.loadSeq&&reference===state.employee&&page===state.page){
            el('hrShiftPage').textContent='Shift history unavailable. Retry selection.';throw error;}}
    }
    async function assign(event) {
        event.preventDefault();
        if(state.busy||state.accessRevoked)return;
        if (!state.employee || !event.currentTarget.reportValidity()) return notice('Select an employee and shift.');
        const workShiftId = Number(el('hrShiftCode').value), effectiveFrom = el('hrShiftFrom').value,
            effectiveTo = el('hrShiftTo').value || null;
        if (!workShiftId || !effectiveFrom || (effectiveTo && effectiveTo < effectiveFrom)) return notice('Invalid shift or effective dates.');
        const payload = { employeeReference: state.employee, workShiftId, effectiveFrom, effectiveTo };
        const signature = JSON.stringify(payload);
        if(signature===state.lastPostedFingerprint)return notice('This shift assignment is already saved. Change employee, shift or dates for a new assignment.',true);
        if (signature !== state.signature) { state.signature = signature; state.requestKey = crypto.randomUUID(); }
        payload.clientRequestId = state.requestKey;
        if (!window.confirm('Assign this shift? Previous effective-dated history will be preserved.')) return;
        state.busy=true;root.inert=true;
        try {
            await api('/api/hr/employees/shifts', 'POST', payload);
            state.lastPostedFingerprint=signature;guard?.markClean();state.page=1;
            try{await loadHistory();notice('Shift assignment recorded.', true);}
            catch(readError){if([401,403].includes(readError.statusCode))handleError(readError);
                notice('Shift assignment recorded. History refresh unavailable: '+readError.message,true);}
        } catch (error) {handleError(error);
            if(!state.accessRevoked)notice(error.message + ' Keep the same form values for a safe retry.');}
        finally {state.busy=false;root.inert=state.accessRevoked;}
    }
    el('hrShiftFind').addEventListener('click',()=>findEmployees().catch(handleError));
    el('hrShiftSearch').addEventListener('input',()=>{++state.searchSeq;});
    el('hrShiftSearch').addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); findEmployees().catch(handleError); } });
    el('hrShiftEmployee').addEventListener('change', () => {
        if(state.busy||state.accessRevoked||!confirmDiscard()){
            el('hrShiftEmployee').value=state.employee||'';return;}
        ++state.searchSeq;state.employee=el('hrShiftEmployee').value||null;
        state.page=1;resetDraft();loadHistory().catch(handleError);
    });
    el('hrShiftForm').addEventListener('submit', assign);
    el('hrShiftPrev').addEventListener('click', () => { if (!state.busy&&state.page > 1) { state.page--; loadHistory().catch(handleError); } });
    el('hrShiftNext').addEventListener('click', () => { if (!state.busy&&state.page < state.pages) { state.page++; loadHistory().catch(handleError); } });
    el('hrShiftFrom').value=localDate();guard?.markClean();
    const initialDate=el('hrShiftFrom').value;
    loadShifts().then(()=>{
        if(!state.employee&&!el('hrShiftCode').value&&!el('hrShiftTo').value&&
            el('hrShiftFrom').value===initialDate)guard?.markClean();
    }).catch(handleError);
})();
