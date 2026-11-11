(() => {
    'use strict';
    const root = document.getElementById('employeeRecords');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value
        || document.querySelector('meta[name="request-verification-token"]')?.content;
    const statuses = {1:'Active',2:'Suspended',3:'Resigned',4:'Terminated',5:'Retired'};
    el('employeeNew').hidden = root.dataset.canCreate !== 'true';
    const state = {reference:null, record:null, page:1, pages:1, historyPage:1, historyPages:1,
        requestId:null,requestSignature:'',busy:false,accessRevoked:false,listSeq:0,detailSeq:0,
        historySeq:0,optionsReady:false};
    const employeeForm=el('employeeForm'),stateForm=el('employeeStateForm');
    const guards={employee:window.EduOSFormGuard?.create(employeeForm),
        status:window.EduOSFormGuard?.create(stateForm)};
    const mayDiscard=()=>Object.values(guards).every(guard=>!guard?.isDirty()||guard.confirmDiscard());
    const identity=[document.body?.dataset?.authTenantId||'',document.body?.dataset?.authSessionReference||'',
        document.body?.dataset?.authUserId||''];
    const sameSession=()=>identity[0]===(document.body?.dataset?.authTenantId||'')&&
        identity[1]===(document.body?.dataset?.authSessionReference||'')&&
        identity[2]===(document.body?.dataset?.authUserId||'');
    const today = () => {const d=new Date(); return [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');};
    const clean = value => typeof value === 'string' ? value.trim() : value;
    const val = id => clean(el(id).value);
    function message(text, error=false) {
        if(!sameSession())return;
        const box=el('employeeMessage'); box.className='alert '+(error?'alert-danger':'alert-success');
        box.textContent=text; box.focus();
    }
    function handleError(error) {
        if([401,403].includes(error.statusCode)||!sameSession()) {
            state.accessRevoked=true;++state.listSeq;++state.detailSeq;++state.historySeq;
            state.record=null;state.reference=null;state.requestId=null;state.requestSignature='';
            el('employeeRows').replaceChildren();el('employeeHistoryRows').replaceChildren();
            el('employeeDesignation').replaceChildren(new Option('Access unavailable',''));
            el('employeeDepartment').replaceChildren(new Option('Access unavailable',''));
            employeeForm.reset();stateForm.reset();guards.employee?.markClean();guards.status?.markClean();
            el('employeeEditorPanel').hidden=true;el('employeeStatusPanel').hidden=true;
            el('employeeHistoryPanel').hidden=true;root.inert=true;
        }
        message(error.message,true);
    }
    async function api(url, method='GET', body) {
        if(!sameSession()||state.accessRevoked)throw Error('Session changed. Reload this page.');
        const headers={Accept:'application/json'};
        if(method!=='GET') { if(!token) throw Error('Security token missing. Reload this page.'); headers.RequestVerificationToken=token; }
        if(body!==undefined) headers['Content-Type']='application/json';
        const res=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,
            ...(body===undefined?{}:{body:JSON.stringify(body)})});
        const result=await res.json().catch(()=>null);
        if(!sameSession())throw Error('Session changed. Reload this page.');
        if(!res.ok||!result?.success){const error=Error(result?.message||'Unable to complete request.');
            error.statusCode=res.status;throw error;}
        return result.data;
    }
    function td(value) {const cell=document.createElement('td');cell.textContent=value===null||value===undefined||value===''?'—':String(value);return cell;}
    function empty(id,count,text) {const row=document.createElement('tr'),cell=td(text);cell.colSpan=count;row.append(cell);el(id).replaceChildren(row);}
    function pager(prefix,page,pages,total) {
        el(prefix+'Page').textContent='Page '+page+' of '+pages+(total===undefined?'':' · '+total+' records');
        el(prefix+'Prev').disabled=page<=1;el(prefix+'Next').disabled=page>=pages;
    }
    async function loadList() {
        if(state.accessRevoked)return false;
        const seq=++state.listSeq,requestedPage=state.page,search=val('employeeSearch');
        empty('employeeRows',5,'Loading employees…');
        try {
            const qs=new URLSearchParams({page:String(requestedPage),pageSize:'15'});
            if(search) qs.set('search',search);
            const data=await api('/api/hr/employees?'+qs);
            if(seq!==state.listSeq||state.accessRevoked||requestedPage!==state.page||search!==val('employeeSearch'))return false;
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
            return true;
        } catch(e) {if(seq===state.listSeq){empty('employeeRows',5,'Unable to load employee directory.');handleError(e);}return false;}
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
            if(!sameSession()||state.accessRevoked)return false;
            state.optionsReady=true;d.disabled=false;org.disabled=false;
            return true;
        } catch(e) {handleError(e);return false;}
    }
    function editPanel(show) {
        el('employeeEditorPanel').hidden=!show;
        if(show) el('employeeEditorPanel').scrollIntoView({block:'nearest',behavior:'auto'});
    }
    function newEmployee() {
        if(state.busy||state.accessRevoked||!state.optionsReady||!mayDiscard())return false;
        ++state.detailSeq;++state.historySeq;
        state.reference=null;state.record=null;state.requestId=null;state.requestSignature='';
        employeeForm.inert=false;stateForm.inert=false;
        employeeForm.reset();el('employeeJoin').value=today();
        el('employeeCode').readOnly=false;el('employeeJoin').readOnly=false;
        el('employeeEditorTitle').textContent='Register employee';el('employeeSave').textContent='Register employee';el('employeeSave').disabled=false;
        el('employeeStatusPanel').hidden=true;el('employeeHistoryPanel').hidden=true;
        guards.employee?.markClean();guards.status?.markClean();editPanel(true);return true;
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
        guards.employee?.markClean();guards.status?.markClean();
        editPanel(true);loadHistory();
    }
    async function openRecord(ref) {
        if(state.busy||state.accessRevoked||!state.optionsReady||!mayDiscard())return false;
        const seq=++state.detailSeq;
        employeeForm.inert=true;stateForm.inert=true;
        try {const data=await api('/api/hr/employees/'+encodeURIComponent(ref));
            if(seq!==state.detailSeq||state.accessRevoked)return false;
            fillRecord(data);return true;
        }catch(e){if(seq===state.detailSeq)handleError(e);return false;}
        finally{if(seq===state.detailSeq&&!state.busy&&!state.accessRevoked){
            employeeForm.inert=false;stateForm.inert=false;}}
    }
    async function save(event) {
        event.preventDefault();if(state.busy||state.accessRevoked||!state.optionsReady||!sameSession()||
            !employeeForm.reportValidity())return;
        const request={organizationUnitId:val('employeeDepartment')?Number(val('employeeDepartment')):null,
            designationId:Number(val('employeeDesignation')),fullName:val('employeeName'),
            phone:val('employeePhone')||null,email:val('employeeEmail')||null,
            address:val('employeeAddress')||null,canTeach:el('employeeTeacher').checked,photoUrl:null};
        if(!request.designationId){message('Select a designation.',true);return;}
        const creating=!state.reference;
        if(creating) {
            Object.assign(request,{employeeCode:val('employeeCode'),joiningDate:val('employeeJoin')});
            const signature=JSON.stringify(request);
            if(state.requestSignature!==signature){state.requestSignature=signature;state.requestId=crypto.randomUUID();}
            request.clientRequestId=state.requestId;
        }
        else {request.rowVersion=state.record.rowVersion;request.photoUrl=state.record.photoUrl||null;}
        state.busy=true;root.inert=true;el('employeeSave').disabled=true;
        let committed=false;
        try {
            const data=await api(creating?'/api/hr/employees':'/api/hr/employees/'+encodeURIComponent(state.reference),
                creating?'POST':'PUT',request);
            committed=true;state.requestId=null;state.requestSignature='';
            fillRecord(data);
            const refreshed=await loadList();
            message(refreshed?(creating?'Employee registered.':'Employee updated.'):
                'Employee saved, but the directory could not refresh. Reload to verify.',!refreshed);
        } catch(e){handleError(e);if(!committed&&!state.accessRevoked)
            message(e.message+' If the connection failed after Save, check the directory before retrying.',true);}
        finally{state.busy=false;root.inert=state.accessRevoked;
            el('employeeSave').disabled=state.accessRevoked||Boolean(state.record&&[3,4,5].includes(Number(state.record.state)));}
    }
    async function saveState(event) {
        event.preventDefault();if(state.busy||state.accessRevoked||!sameSession()||!state.reference||
            !state.record?.rowVersion||!stateForm.reportValidity())return;
        const next=Number(val('employeeState')),terminal=[3,4,5].includes(next);
        if(!next){message('Choose a new employment status.',true);return;}
        if(terminal&&(!val('employeeLeaving')||!val('employeeReason'))) {
            message('Leaving date and reason are required for an employment exit.',true);return;
        }
        if(!window.confirm('Change employee employment status to '+statuses[next]+'? This change is audited and may be irreversible.'))return;
        state.busy=true;root.inert=true;el('employeeStateSave').disabled=true;
        let committed=false;
        try {
            const result=await api('/api/hr/employees/'+encodeURIComponent(state.reference)+'/state','POST',
                {state:next,leavingDate:terminal?val('employeeLeaving'):null,reason:val('employeeReason')||null,rowVersion:state.record.rowVersion});
            committed=true;fillRecord(result);
            const refreshed=await loadList();
            message(refreshed?'Employment status updated.':
                'Employment status updated; directory refresh unavailable. Reload to verify.',!refreshed);
        } catch(e){handleError(e);if(!committed&&!state.accessRevoked)
            message(e.message+' Check current status before attempting this transition again.',true);}
        finally{state.busy=false;root.inert=state.accessRevoked;el('employeeStateSave').disabled=state.accessRevoked;}
    }
    async function loadHistory() {
        if(!state.reference||state.accessRevoked)return false;
        const seq=++state.historySeq,ref=state.reference,requestedPage=state.historyPage;
        empty('employeeHistoryRows',6,'Loading history…');
        try {
            const data=await api('/api/hr/employees/'+encodeURIComponent(ref)+'/assignment-history?'+
                new URLSearchParams({page:String(requestedPage),pageSize:'10'}));
            if(seq!==state.historySeq||state.accessRevoked||ref!==state.reference||requestedPage!==state.historyPage)return false;
            state.historyPages=Math.max(1,Math.ceil((data.totalCount||0)/10));
            el('employeeHistoryRows').replaceChildren();
            for(const item of data.items||[]) {
                const row=document.createElement('tr');
                [item.effectiveFrom,item.effectiveTo,item.campusId,item.organizationUnitId,item.designationId,item.reason].forEach(x=>row.append(td(x)));
                el('employeeHistoryRows').append(row);
            }
            if(!data.items?.length)empty('employeeHistoryRows',6,'No assignment history.');
            pager('employeeHistory',state.historyPage,state.historyPages,data.totalCount||0);
            return true;
        }catch(e){if(seq===state.historySeq){empty('employeeHistoryRows',6,'Unable to load assignment history.');handleError(e);}return false;}
    }
    function closeEditor() {
        if(state.busy||state.accessRevoked||!mayDiscard())return false;
        ++state.detailSeq;++state.historySeq;
        guards.employee?.markClean();guards.status?.markClean();editPanel(false);
        return true;
    }
    el('employeeLookup').addEventListener('submit',e=>{
        e.preventDefault();if(state.busy||state.accessRevoked)return;
        state.page=1;loadList();
    });
    el('employeeNew').addEventListener('click',newEmployee);
    el('employeeForm').addEventListener('submit',save);
    el('employeeStateForm').addEventListener('submit',saveState);
    for(const id of ['employeeClose','employeeCancel'])el(id).addEventListener('click',closeEditor);
    el('employeePrev').addEventListener('click',()=>{
        if(state.busy||state.accessRevoked)return;
        if(state.page>1){--state.page;loadList();}
    });
    el('employeeNext').addEventListener('click',()=>{
        if(state.busy||state.accessRevoked)return;
        if(state.page<state.pages){++state.page;loadList();}
    });
    el('employeeHistoryPrev').addEventListener('click',()=>{
        if(state.busy||state.accessRevoked)return;
        if(state.historyPage>1){--state.historyPage;loadHistory();}
    });
    el('employeeHistoryNext').addEventListener('click',()=>{
        if(state.busy||state.accessRevoked)return;
        if(state.historyPage<state.historyPages){++state.historyPage;loadHistory();}
    });
    loadOptions().then(async ready=>{
        if(!ready||state.accessRevoked)return;
        await loadList();
        const ref=new URLSearchParams(location.search).get('reference');
        if(ref&&/^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(ref))openRecord(ref);
    });
})();
