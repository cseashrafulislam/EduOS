(() => {
    'use strict';
    const root = document.getElementById('hrCampusWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const form = el('hrCampusForm'), guard = window.EduOSFormGuard?.create(form);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
        document.querySelector('meta[name="request-verification-token"]')?.content;
    const identity = [document.body?.dataset?.authTenantId || '',
        document.body?.dataset?.authSessionReference || '', document.body?.dataset?.authUserId || ''];
    const sameSession = () => identity[0] === (document.body?.dataset?.authTenantId || '') &&
        identity[1] === (document.body?.dataset?.authSessionReference || '') &&
        identity[2] === (document.body?.dataset?.authUserId || '');
    const state = {employee:null,page:1,pages:1,requestKey:null,signature:null,busy:false,
        accessRevoked:false,campusesReady:false,searchSeq:0,historySeq:0};
    const today = () => {const d = new Date();
        return [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');};
    const date = value => value ? String(value).slice(0,10) : '';
    const cell = value => {const td = document.createElement('td');
        td.textContent = value == null || value === '' ? '—' : String(value);return td;};
    const option = (value,label) => new Option(label,String(value));
    function notice(message,success=false) {
        if (!sameSession()) return;
        const box = el('hrCampusAlert');box.className = 'alert alert-' + (success ? 'success' : 'danger');
        box.textContent = message;box.focus();
    }
    function updateSave() {
        el('hrCampusSave').disabled = state.busy || state.accessRevoked ||
            !state.campusesReady || !state.employee;
    }
    function emptyHistory(message='Select an employee.') {
        const tbody = el('hrCampusRows'), tr = document.createElement('tr'), td = cell(message);
        td.colSpan = 5;tr.append(td);tbody.replaceChildren(tr);
        el('hrCampusPage').textContent = '';el('hrCampusPrev').disabled = true;
        el('hrCampusNext').disabled = true;
    }
    function resetDraft() {
        form.reset();el('hrCampusFrom').value = today();
        state.signature = null;state.requestKey = null;guard?.markClean();updateSave();
    }
    function handleError(error) {
        if ([401,403].includes(error.statusCode) || !sameSession()) {
            state.accessRevoked = true;++state.searchSeq;++state.historySeq;
            state.employee = null;state.requestKey = null;state.signature = null;
            el('hrCampusEmployee').replaceChildren(option('','Access unavailable'));
            el('hrCampusId').replaceChildren(option('','Access unavailable'));
            el('hrCampusRows').replaceChildren();el('hrCampusPage').textContent = '';
            form.reset();guard?.markClean();root.inert = true;updateSave();
        }
        notice(error.message);
    }
    async function api(path,method='GET',body) {
        if (!sameSession() || state.accessRevoked) throw Error('Session changed. Reload this page.');
        const headers = {Accept:'application/json'};
        if (method !== 'GET') {
            if (!token) throw Error('Security token missing. Reload this page.');
            headers.RequestVerificationToken = token;
        }
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        const response = await fetch(path,{method,credentials:'same-origin',cache:'no-store',headers,
            ...(body === undefined ? {} : {body:JSON.stringify(body)})});
        const result = await response.json().catch(() => null);
        if (!sameSession()) throw Error('Session changed. Reload this page.');
        if (!response.ok || !result?.success) {
            const error = Error(result?.message || 'Employee campus request failed.');
            error.statusCode = response.status;throw error;
        }
        return result.data;
    }
    async function findEmployees() {
        if (state.busy || state.accessRevoked) return false;
        const search = el('hrCampusSearch').value.trim();
        if (search.length < 2 || search.length > 100) return notice('Enter 2–100 characters to search employees.'),false;
        if (guard?.isDirty() && !guard.confirmDiscard()) return false;
        const seq = ++state.searchSeq;
        el('hrCampusFind').disabled = true;
        try {
            const data = await api('/api/hr/employees?' +
                new URLSearchParams({search,page:'1',pageSize:'50'}));
            if (seq !== state.searchSeq || state.accessRevoked ||
                search !== el('hrCampusSearch').value.trim()) return false;
            const select = el('hrCampusEmployee');
            select.replaceChildren(option('','Choose employee'));
            for (const employee of data?.items || [])
                select.add(option(employee.reference,employee.employeeCode + ' — ' + employee.fullName));
            state.employee = null;state.page = 1;++state.historySeq;
            resetDraft();emptyHistory();
            if (!data?.items?.length) notice('No matching employees.');
            return true;
        } catch (error) {if (seq === state.searchSeq) handleError(error);return false;}
        finally {if (seq === state.searchSeq) el('hrCampusFind').disabled = state.accessRevoked;}
    }
    async function loadCampuses() {
        try {
            const campuses = await api('/api/institution-onboarding/campus-list');
            if (state.accessRevoked) return false;
            const select = el('hrCampusId');select.replaceChildren(option('','Choose active campus'));
            for (const campus of campuses || []) if (campus.isActive)
                select.add(option(campus.id,campus.name + ' (' + campus.code + ')'));
            state.campusesReady = true;select.disabled = false;
            guard?.markClean();updateSave();return true;
        } catch (error) {handleError(error);return false;}
    }
    async function loadHistory() {
        const reference = state.employee, requestedPage = state.page, seq = ++state.historySeq;
        if (!reference || state.accessRevoked) {emptyHistory();return false;}
        emptyHistory('Loading campus history…');
        try {
            const data = await api('/api/hr/employees/' + encodeURIComponent(reference) +
                '/campus-assignments?' + new URLSearchParams({page:String(requestedPage),pageSize:'20'}));
            if (seq !== state.historySeq || state.accessRevoked || reference !== state.employee ||
                requestedPage !== state.page) return false;
            const tbody = el('hrCampusRows');tbody.replaceChildren();
            for (const row of data?.items || []) {
                const tr = document.createElement('tr');
                [row.campusName,row.isPrimary ? 'Yes' : 'No',date(row.effectiveFrom),
                    date(row.effectiveTo) || 'Open',row.isCurrent ? 'Current' : 'Historical']
                    .forEach(value => tr.append(cell(value)));
                tbody.append(tr);
            }
            if (!data?.items?.length) emptyHistory('No campus assignments.');
            state.pages = Math.max(1,Math.ceil(Number(data?.totalCount || 0)/20));
            el('hrCampusPage').textContent = 'Page ' + state.page + ' of ' + state.pages +
                ' · ' + Number(data?.totalCount || 0) + ' records';
            el('hrCampusPrev').disabled = state.page <= 1;
            el('hrCampusNext').disabled = state.page >= state.pages;
            return true;
        } catch (error) {if (seq === state.historySeq) {emptyHistory('Unable to load campus history.');handleError(error);}return false;}
    }
    async function assign(event) {
        event.preventDefault();
        if (state.busy || state.accessRevoked || !state.campusesReady || !state.employee ||
            !form.reportValidity()) return notice('Select an employee and active campus.');
        const campusId = Number(el('hrCampusId').value),effectiveFrom = el('hrCampusFrom').value,
            effectiveTo = el('hrCampusTo').value || null,isPrimary = el('hrCampusPrimary').checked;
        if (!Number.isSafeInteger(campusId) || campusId <= 0 || !effectiveFrom ||
            (effectiveTo && effectiveTo < effectiveFrom)) return notice('Invalid campus or effective dates.');
        const payload = {employeeReference:state.employee,campusId,effectiveFrom,effectiveTo,isPrimary};
        const signature = JSON.stringify(payload);
        if (signature !== state.signature) {state.signature = signature;state.requestKey = crypto.randomUUID();}
        if (!window.confirm('Assign this campus? Existing assignment periods may be closed.')) return;
        state.busy = true;root.inert = true;updateSave();
        let committed = false;
        try {
            await api('/api/hr/employees/campuses','POST',{...payload,clientRequestId:state.requestKey});
            committed = true;state.page = 1;resetDraft();
            const refreshed = await loadHistory();
            notice(refreshed ? 'Campus assignment recorded.' :
                'Campus assignment recorded; history could not refresh. Reload to verify.',true);
        } catch (error) {
            handleError(error);
            if (!committed && !state.accessRevoked)
                notice(error.message + ' Check campus history before retrying an uncertain submission.');
        } finally {state.busy = false;root.inert = state.accessRevoked;updateSave();}
    }
    el('hrCampusFind').addEventListener('click',findEmployees);
    el('hrCampusSearch').addEventListener('keydown',event => {
        if (event.key === 'Enter') {event.preventDefault();findEmployees();}
    });
    el('hrCampusEmployee').addEventListener('change',() => {
        const selected = el('hrCampusEmployee').value || null;
        if (state.busy || state.accessRevoked || guard?.isDirty() && !guard.confirmDiscard()) {
            el('hrCampusEmployee').value = state.employee || '';return;
        }
        state.employee = selected;state.page = 1;++state.historySeq;
        resetDraft();loadHistory();
    });
    form.addEventListener('submit',assign);
    el('hrCampusPrev').addEventListener('click',() => {
        if (!state.busy && !state.accessRevoked && state.page > 1) {--state.page;loadHistory();}
    });
    el('hrCampusNext').addEventListener('click',() => {
        if (!state.busy && !state.accessRevoked && state.page < state.pages) {++state.page;loadHistory();}
    });
    el('hrCampusFrom').value = today();guard?.markClean();updateSave();
    loadCampuses();
})();
