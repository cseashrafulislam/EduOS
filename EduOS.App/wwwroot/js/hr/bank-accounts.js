(() => {
    'use strict';
    const root = document.getElementById('hrBankWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    const state = { employee: null, editing: null, searchSeq: 0, loadSeq: 0,
        busy: false, accessRevoked: false, baseline: '' };
    // Do not use the shared form fingerprint here: it would retain the plaintext
    // account number in a long-lived closure. Only nonsecret fields are snapshotted.
    function nonSecretSnapshot() {
        return JSON.stringify([state.editing?.id || null,el('hrBankName').value,
            el('hrBankAccountName').value,el('hrBankBranch').value,
            el('hrBankRouting').value,el('hrBankCurrency').value,
            el('hrBankPrimary').checked,el('hrBankActive').checked]);
    }
    function markClean() { state.baseline=nonSecretSnapshot(); }
    function dirty() { return !!el('hrBankNumber').value || nonSecretSnapshot()!==state.baseline; }
    function mayDiscard() { return !dirty() || window.confirm('Discard unsaved payroll bank account details?'); }
    window.addEventListener('beforeunload',event=>{
        if(!dirty())return;
        event.preventDefault();event.returnValue='';
    });
    function notice(message, success = false) {
        const box = el('hrBankAlert');
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
        const response = await fetch(path, {
            method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success){const error=new Error(result?.message || 'Request failed.');
            error.statusCode=response.status;throw error;}
        return result.data;
    }
    function handleError(error) {
        if([401,403].includes(error.statusCode)){
            state.accessRevoked=true;++state.searchSeq;++state.loadSeq;state.employee=null;
            el('hrBankRows').replaceChildren();el('hrBankEmployee').replaceChildren(option('','Access unavailable'));
            resetForm();root.inert=true;
        }
        notice(error.message);
    }
    const cell = value => {
        const td = document.createElement('td');
        td.textContent = value == null || value === '' ? '—' : String(value);
        return td;
    };
    const option = (value, label) => {
        const o = document.createElement('option');
        o.value = value;
        o.textContent = label;
        return o;
    };
    function resetForm() {
        el('hrBankForm').reset();
        el('hrBankNumber').value = '';
        el('hrBankCurrency').value = 'BDT';
        el('hrBankActive').checked = true;
        state.editing = null;
        el('hrBankFormTitle').textContent = 'Register bank account';
        el('hrBankSave').textContent = 'Save protected account';
        markClean();
    }
    function empty(message) {
        const body = el('hrBankRows');
        const tr = document.createElement('tr');
        const td = cell(message);
        td.colSpan = 7;
        tr.append(td);
        body.replaceChildren(tr);
    }
    async function searchEmployees(event) {
        event.preventDefault();
        if(state.busy||state.accessRevoked||!mayDiscard())return;
        const term = el('hrBankSearch').value.trim();
        if (term.length < 2 || term.length > 100) return notice('Enter 2–100 characters.');
        const sequence = ++state.searchSeq;
        const select = el('hrBankEmployee');
        el('hrBankFind').disabled=true;
        try {
            const data = await api('/api/hr/employees?search=' + encodeURIComponent(term) + '&page=1&pageSize=50');
            if (sequence !== state.searchSeq||el('hrBankSearch').value.trim()!==term||state.busy||!mayDiscard())return;
            ++state.loadSeq;state.employee=null;resetForm();empty('Select an employee.');
            select.replaceChildren(option('', 'Choose employee'));
            for (const employee of data?.items || [])
                select.append(option(employee.reference, employee.employeeCode + ' — ' + employee.fullName));
            if (!(data?.items || []).length) notice('No matching employees.');
        } catch (error) {
            if (sequence === state.searchSeq) handleError(error);
        }finally{if(sequence===state.searchSeq)el('hrBankFind').disabled=false;}
    }
    async function loadAccounts() {
        const employee = state.employee, sequence = ++state.loadSeq;
        if (!employee) return empty('Select an employee.');
        empty('Loading protected accounts…');
        try {
            const rows = await api('/api/hr/employees/' + encodeURIComponent(employee) + '/bank-accounts');
            if (sequence !== state.loadSeq || state.employee !== employee) return;
            const body = el('hrBankRows');
            body.replaceChildren();
            for (const row of rows || []) {
                const tr = document.createElement('tr');
                [row.bankName, row.accountName, '•••• ' + (row.accountNumberLast4 || '—'),
                    row.branchName, row.isPrimary ? 'Yes' : 'No', row.isActive ? 'Yes' : 'No']
                    .forEach(value => tr.append(cell(value)));
                const action = document.createElement('td');
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn btn-sm btn-outline-primary';
                button.textContent = 'Edit';
                button.addEventListener('click', () => editAccount(row));
                action.append(button);
                tr.append(action);
                body.append(tr);
            }
            if (!(rows || []).length) empty('No bank accounts registered.');
        } catch (error) {
            if (sequence === state.loadSeq && state.employee === employee){
                empty('Unable to load accounts.');throw error;}
        }
    }
    function editAccount(row) {
        if (state.busy||state.accessRevoked||!mayDiscard()) return;
        resetForm();
        state.editing = { id: row.id, rowVersion: row.rowVersion };
        el('hrBankName').value = row.bankName || '';
        el('hrBankAccountName').value = row.accountName || '';
        el('hrBankBranch').value = row.branchName || '';
        el('hrBankRouting').value = row.routingNumber || '';
        el('hrBankCurrency').value = row.currencyCode || 'BDT';
        el('hrBankPrimary').checked = Boolean(row.isPrimary);
        el('hrBankActive').checked = Boolean(row.isActive);
        el('hrBankFormTitle').textContent = 'Edit protected account (ending ' + (row.accountNumberLast4 || '—') + ')';
        el('hrBankSave').textContent = 'Update protected account';
        markClean();
        el('hrBankNumber').focus();
    }
    async function saveAccount(event) {
        event.preventDefault();
        if (state.busy||state.accessRevoked||!state.employee) return notice('Select an employee first.');
        if (!event.currentTarget.reportValidity()) return;
        if (state.editing && !state.editing.rowVersion) return notice('Missing concurrency version; reload accounts.');
        const numberField = el('hrBankNumber');
        const accountNumber = numberField.value.trim();
        if (!accountNumber || accountNumber.length > 200) return notice('Enter a valid account number.');
        const payload = {
            employeeReference: state.employee, bankName: el('hrBankName').value.trim(),
            accountName: el('hrBankAccountName').value.trim(), accountNumber,
            branchName: el('hrBankBranch').value.trim() || null,
            routingNumber: el('hrBankRouting').value.trim() || null,
            currencyCode: el('hrBankCurrency').value, isPrimary: el('hrBankPrimary').checked,
            isActive: el('hrBankActive').checked,
            rowVersion: state.editing?.rowVersion || null
        };
        const editing = state.editing;
        if (!window.confirm((editing ? 'Update' : 'Register') + ' this protected payroll bank account?')) {
            numberField.value = '';
            return;
        }
        state.busy = true;root.inert=true;
        try {
            await api(editing ? '/api/hr/employees/bank-accounts/' + editing.id : '/api/hr/employees/bank-accounts',
                editing ? 'PUT' : 'POST', payload);
            resetForm();
            try{await loadAccounts();notice('Protected bank account saved.', true);}
            catch(readError){if([401,403].includes(readError.statusCode))handleError(readError);
                notice('Protected bank account saved. Register refresh unavailable: '+readError.message,true);}
        } catch (error) {
            handleError(error);
            if(!state.accessRevoked)notice(error.message + ' Check the masked register and re-enter the full number before any retry.');
        } finally {
            // Never retain plaintext account numbers in form fields after a submission attempt.
            numberField.value = '';
            payload.accountNumber = '';
            state.busy = false;
            root.inert=state.accessRevoked;
        }
    }
    el('hrBankSearchForm').addEventListener('submit', searchEmployees);
    el('hrBankSearch').addEventListener('input',()=>{++state.searchSeq;el('hrBankFind').disabled=false;});
    el('hrBankEmployee').addEventListener('change', () => {
        if(state.busy||state.accessRevoked||!mayDiscard()){
            el('hrBankEmployee').value=state.employee||'';return;}
        ++state.searchSeq;
        state.employee = el('hrBankEmployee').value || null;
        resetForm();
        loadAccounts().catch(handleError);
    });
    el('hrBankForm').addEventListener('submit', saveAccount);
    el('hrBankCancel').addEventListener('click',()=>{
        if(state.busy||state.accessRevoked||!mayDiscard())return;resetForm();
    });
    markClean();
})();
