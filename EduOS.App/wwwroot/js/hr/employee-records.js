(() => {
    'use strict';
    const root = document.getElementById('employeeRecords');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value
        || document.querySelector('meta[name="request-verification-token"]')?.content;
    const statuses = {1:'Active',2:'Suspended',3:'Resigned',4:'Terminated',5:'Retired'};
    const state = {reference:null, record:null, page:1, pages:1, historyPage:1, historyPages:1, requestId:null, busy:false};
    const today = () => {const d=new Date(); return [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');};
    const clean = value => typeof value === 'string' ? value.trim() : value;
    const val = id => clean(el(id).value);
    function message(text, error=false) {
        const box=el('employeeMessage'); box.className='alert '+(error?'alert-danger':'alert-success');
        box.textContent=text; box.focus();
    }
    async function api(url, method='GET', body) {
        const headers={Accept:'application/json'};
        if(method!=='GET') { if(!token) throw Error('Security token missing. Reload this page.'); headers.RequestVerificationToken=token; }
        if(body!==undefined) headers['Content-Type']='application/json';
        const res=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,
            ...(body===undefined?{}:{body:JSON.stringify(body)})});
        const result=await res.json().catch(()=>null);
        if(!res.ok||!result?.success) throw Error(result?.message||'Unable to complete request.');
        return result.data;
    }
    function td(value) {const cell=document.createElement('td');cell.textContent=value===null||value===undefined||value===''?'—':String(value);return cell;}
    function empty(id,count,text) {const row=document.createElement('tr'),cell=td(text);cell.colSpan=count;row.append(cell);el(id).replaceChildren(row);}
    function pager(prefix,page,pages,total) {
        el(prefix+'Page').textContent='Page '+page+' of '+pages+(total===undefined?'':' · '+total+' records');
        el(prefix+'Prev').disabled=page<=1;el(prefix+'Next').disabled=page>=pages;
    }
    async function loadList() {
        empty('employeeRows',5,'Loading employees…');
        try {
            const qs=new URLSearchParams({page:String(state.page),pageSize:'15'});
            if(val('employeeSearch')) qs.set('search',val('employeeSearch'));
            const data=await api('/api/hr/employees?'+qs);
            state.pages=Math.max(1,Math.ceil((data.totalCount||0)/15));
            state.page=Number(data.page)||state.page;el('employeeRows').replaceChildren();
            for(const item of data.items||[]) {
                const tr=document.createElement('tr');
                [item.employeeCode,item.fullName,item.designationName,statuses[item.state]||item.state].forEach(x=>tr.append(td(x)));
                const actions=document.createElement('td'),btn=document.createElement('button');
                btn.type='button';btn.className='btn btn-sm btn-outline-primary';btn.textContent='Open';
                btn.addEventListener('click',()=>openRecord(item.reference));actions.append(btn);tr.append(actions);el('employeeRows').append(tr);
            }
            if(!data.items?.length) empty('employeeRows',5,'No employees found.');
            pager('employee',state.page,state.pages,data.totalCount||0);
        } catch(e) {empty('employeeRows',5,'Unable to load employee directory.');message(e.message,true);}
    }
    async function loadOptions() {
        const d=el('employeeDesignation'),org=el('employeeDepartment');
        d.disabled=true;org.disabled=true;
        try {
            const [designations,first]=await Promise.all([api('/api/hr/setup/designations'),
                api('/api/hr/setup/departments?page=1&pageSize=100')]);
            d.replaceChildren(new Option('Select designation',''));
            for(const item of designations||[]) if(item.isActive) d.add(new Option(item.name,item.id));
            org.replaceChildren(new Option('No department',''));
            const addUnits=units=>{for(const item of units||[]) if(item.isActive) org.add(new Option(item.name,item.id));};
            addUnits(first.items);
            const pages=Math.ceil((first.totalCount||0)/100);
            if(pages>50) throw Error('Department list is too large. Contact administrator.');
            for(let page=2;page<=pages;page++) addUnits((await api('/api/hr/setup/departments?page='+page+'&pageSize=100')).items);
            d.disabled=false;org.disabled=false;
        } catch(e) {message(e.message,true);}
    }
    function editPanel(show) {
        el('employeeEditorPanel').hidden=!show;
        if(show) el('employeeEditorPanel').scrollIntoView({block:'nearest',behavior:'auto'});
    }
    function newEmployee() {
        state.reference=null;state.record=null;state.requestId=crypto.randomUUID();
        el('employeeForm').reset();el('employeeJoin').value=today();
        el('employeeCode').readOnly=false;el('employeeJoin').readOnly=false;
        el('employeeEditorTitle').textContent='Register employee';el('employeeSave').textContent='Register employee';el('employeeSave').disabled=false;
        el('employeeStatusPanel').hidden=true;el('employeeHistoryPanel').hidden=true;editPanel(true);
    }
    function fillRecord(data) {
        state.record=data;state.reference=data.reference;state.historyPage=1;
        el('employeeCode').value=data.employeeCode||'';el('employeeCode').readOnly=true;
        el('employeeName').value=data.fullName||'';
        el('employeeDesignation').value=String(data.designationId||'');
        el('employeeDepartment').value=data.organizationUnitId==null?'':String(data.organizationUnitId);
        el('employeeJoin').value=String(data.joiningDate||'').slice(0,10);el('employeeJoin').readOnly=true;
        el('employeePhone').value=data.phone||'';el('employeeEmail').value=data.email||'';
        el('employeeAddress').value=data.address||'';el('employeeTeacher').checked=Boolean(data.canTeach);
        el('employeeEditorTitle').textContent='Edit employee · '+data.employeeCode;
        el('employeeSave').textContent='Save changes';
        const terminal=[3,4,5].includes(Number(data.state));
        el('employeeSave').disabled=terminal;
        const allowed=Number(data.state)===1?[2,3,4,5]:Number(data.state)===2?[1,3,4,5]:[];
        el('employeeState').replaceChildren(new Option('Choose status',''));
        for(const next of allowed) el('employeeState').add(new Option(statuses[next],next));
        el('employeeLeaving').value='';el('employeeReason').value='';
        el('employeeStatusPanel').hidden=!allowed.length;
        el('employeeHistoryPanel').hidden=false;
        editPanel(true);loadHistory();
    }
    async function openRecord(ref) {
        try {const data=await api('/api/hr/employees/'+encodeURIComponent(ref));fillRecord(data);}
        catch(e){message(e.message,true);}
    }
    async function save(event) {
        event.preventDefault();if(state.busy||!el('employeeForm').reportValidity())return;
        const request={organizationUnitId:val('employeeDepartment')?Number(val('employeeDepartment')):null,
            designationId:Number(val('employeeDesignation')),fullName:val('employeeName'),
            phone:val('employeePhone')||null,email:val('employeeEmail')||null,
            address:val('employeeAddress')||null,canTeach:el('employeeTeacher').checked,photoUrl:null};
        if(!request.designationId){message('Select a designation.',true);return;}
        const creating=!state.reference;
        if(creating) Object.assign(request,{clientRequestId:state.requestId,employeeCode:val('employeeCode'),
            joiningDate:val('employeeJoin')});
        else {request.rowVersion=state.record.rowVersion;request.photoUrl=state.record.photoUrl||null;}
        state.busy=true;el('employeeSave').disabled=true;
        try {
            const data=await api(creating?'/api/hr/employees':'/api/hr/employees/'+encodeURIComponent(state.reference),
                creating?'POST':'PUT',request);
            message(creating?'Employee registered.':'Employee updated.');
            if(creating) state.requestId=null;
            fillRecord(data);await loadList();
        } catch(e){message(e.message,true);}
        finally{state.busy=false;el('employeeSave').disabled=Boolean(state.record&&[3,4,5].includes(Number(state.record.state)));}
    }
    async function saveState(event) {
        event.preventDefault();if(state.busy||!state.reference||!state.record?.rowVersion)return;
        const next=Number(val('employeeState')),terminal=[3,4,5].includes(next);
        if(!next){message('Choose a new employment status.',true);return;}
        if(terminal&&(!val('employeeLeaving')||!val('employeeReason'))) {
            message('Leaving date and reason are required for an employment exit.',true);return;
        }
        if(!window.confirm('Change employee employment status to '+statuses[next]+'? This change is audited and may be irreversible.'))return;
        state.busy=true;el('employeeStateSave').disabled=true;
        try {
            const result=await api('/api/hr/employees/'+encodeURIComponent(state.reference)+'/state','POST',
                {state:next,leavingDate:terminal?val('employeeLeaving'):null,reason:val('employeeReason')||null,rowVersion:state.record.rowVersion});
            message('Employment status updated.');fillRecord(result);await loadList();
        } catch(e){message(e.message,true);}
        finally{state.busy=false;el('employeeStateSave').disabled=false;}
    }
    async function loadHistory() {
        if(!state.reference)return;
        empty('employeeHistoryRows',6,'Loading history…');
        try {
            const data=await api('/api/hr/employees/'+encodeURIComponent(state.reference)+'/assignment-history?'+new URLSearchParams({page:String(state.historyPage),pageSize:'10'}));
            state.historyPages=Math.max(1,Math.ceil((data.totalCount||0)/10));
            el('employeeHistoryRows').replaceChildren();
            for(const item of data.items||[]) {
                const row=document.createElement('tr');
                [item.effectiveFrom,item.effectiveTo,item.campusId,item.organizationUnitId,item.designationId,item.reason].forEach(x=>row.append(td(x)));
                el('employeeHistoryRows').append(row);
            }
            if(!data.items?.length)empty('employeeHistoryRows',6,'No assignment history.');
            pager('employeeHistory',state.historyPage,state.historyPages,data.totalCount||0);
        }catch(e){empty('employeeHistoryRows',6,'Unable to load assignment history.');message(e.message,true);}
    }
    el('employeeLookup').addEventListener('submit',e=>{e.preventDefault();state.page=1;loadList();});
    el('employeeNew').addEventListener('click',newEmployee);
    el('employeeForm').addEventListener('submit',save);
    el('employeeStateForm').addEventListener('submit',saveState);
    for(const id of ['employeeClose','employeeCancel'])el(id).addEventListener('click',()=>editPanel(false));
    el('employeePrev').addEventListener('click',()=>{state.page=Math.max(1,state.page-1);loadList();});
    el('employeeNext').addEventListener('click',()=>{state.page=Math.min(state.pages,state.page+1);loadList();});
    el('employeeHistoryPrev').addEventListener('click',()=>{state.historyPage=Math.max(1,state.historyPage-1);loadHistory();});
    el('employeeHistoryNext').addEventListener('click',()=>{state.historyPage=Math.min(state.historyPages,state.historyPage+1);loadHistory();});
    loadOptions().then(async()=>{await loadList();const ref=new URLSearchParams(location.search).get('reference');
        if(ref&&/^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(ref))openRecord(ref);});
})();
