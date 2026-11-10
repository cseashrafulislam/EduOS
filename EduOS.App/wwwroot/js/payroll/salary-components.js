(() => {
    'use strict';
    const root=document.getElementById('payrollComponents');if(!root)return;
    const el=id=>document.getElementById(id);
    const csrf=root.querySelector('input[name="__RequestVerificationToken"]')?.value
        ||document.querySelector('meta[name="request-verification-token"]')?.content;
    const types={1:'Earning',2:'Deduction',3:'Employer contribution'};
    let rows=[],editing=null,busy=false,accessRevoked=false,loadSeq=0;
    const form=el('componentForm'),guard=window.EduOSFormGuard?.create(form);
    const mayDiscard=()=>!guard?.isDirty()||guard.confirmDiscard();
    function notice(text,error=false) {
        const box=el('componentNotice');box.className='alert '+(error?'alert-danger':'alert-success');box.textContent=text;box.focus();
    }
    async function api(url,method='GET',body) {
        const headers={Accept:'application/json'};
        if(method!=='GET') {if(!csrf)throw Error('Security token unavailable. Reload this page.');headers.RequestVerificationToken=csrf;}
        if(body!==undefined) headers['Content-Type']='application/json';
        const res=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,
            ...(body===undefined?{}:{body:JSON.stringify(body)})});
        const json=await res.json().catch(()=>null);
        if(!res.ok||!json?.success){const error=Error(json?.message||'Salary component request failed.');
            error.statusCode=res.status;throw error;}
        return json.data;
    }
    function handleError(error){
        if([401,403].includes(error.statusCode)){
            accessRevoked=true;++loadSeq;rows=[];render();
            editing=null;form.reset();guard?.markClean();root.inert=true;
            el('componentFormTitle').textContent='Access unavailable';
        }
        notice(error.message,true);
    }
    function clear(force=false) {
        if(busy||accessRevoked||!force&&!mayDiscard())return false;
        editing=null;el('componentForm').reset();el('componentActive').checked=true;el('componentType').value='1';
        el('componentFormTitle').textContent='New salary component';el('componentSave').textContent='Save component';
        guard?.markClean();return true;
    }
    function edit(item,force=false) {
        if(busy||accessRevoked||!force&&!mayDiscard())return;
        editing=item;el('componentCode').value=item.code;el('componentName').value=item.name;
        el('componentType').value=String(item.type);el('componentTaxable').checked=Boolean(item.isTaxable);
        el('componentActive').checked=Boolean(item.isActive);
        el('componentFormTitle').textContent='Edit component · '+item.code;
        el('componentSave').textContent='Update component';
        guard?.markClean();
        el('componentFormTitle').scrollIntoView({block:'nearest'});
    }
    function preset(code,name) {
        if(busy||accessRevoked||!mayDiscard())return;
        const existing=rows.find(x=>x.code===code);
        if(existing){edit(existing,true);notice(code+' already exists. Review its settings before updating.');return;}
        clear(true);el('componentCode').value=code;el('componentName').value=name;
        el('componentType').value='2';el('componentTaxable').checked=false;
        el('componentFormTitle').textContent='Set up '+name;
        el('componentCode').focus();
    }
    function textCell(value) {const c=document.createElement('td');c.textContent=String(value??'');return c;}
    function render() {
        const q=el('componentFilter').value.trim().toLowerCase();
        const filtered=rows.filter(x=>!q||x.code.toLowerCase().includes(q)||x.name.toLowerCase().includes(q));
        const body=el('componentRows');body.replaceChildren();
        for(const item of filtered) {
            const tr=document.createElement('tr');
            [item.code,item.name,types[item.type]||item.type,item.isTaxable?'Yes':'No',
                item.isActive?'Active':'Inactive'].forEach(v=>tr.append(textCell(v)));
            const actions=document.createElement('td'),button=document.createElement('button');
            button.className='btn btn-sm btn-outline-primary';button.type='button';button.textContent='Edit';
            button.addEventListener('click',()=>edit(item));
            actions.append(button);tr.append(actions);body.append(tr);
        }
        if(!filtered.length) {
            const tr=document.createElement('tr'),cell=textCell('No matching salary components.');
            cell.colSpan=6;tr.append(cell);body.append(tr);
        }
        el('componentCount').textContent=filtered.length+' of '+rows.length+' components';
    }
    async function load() {
        if(accessRevoked)return false;
        const request=++loadSeq;
        try {
            const data=await api('/api/hr-payroll/salary-components');
            if(request!==loadSeq||accessRevoked)return false;
            rows=Array.isArray(data)?data:[];
            render();
            if(rows.length>=500)notice('Component register reached the server limit of 500. Contact administrator before adding more.',true);
            return true;
        }catch(e){if(request===loadSeq)handleError(e);return false;}
    }
    async function save(event) {
        event.preventDefault();if(busy||accessRevoked||!form.reportValidity())return;
        const payload={code:el('componentCode').value.trim(),name:el('componentName').value.trim(),
            type:Number(el('componentType').value),isTaxable:el('componentTaxable').checked,
            isActive:el('componentActive').checked,rowVersion:editing?.rowVersion||null};
        if(!payload.code||!payload.name||!types[payload.type])return notice('Enter valid component details.',true);
        if(payload.isActive&&payload.code.toUpperCase().startsWith('STAT_')&&
            (!editing||!editing.isActive)&&!confirm('Activate '+payload.code+'? Every eligible employee will need an explicit salary structure deduction (including reviewed zero exemptions) before payroll can run.'))return;
        if(editing?.isActive&&!payload.isActive&&
            !confirm('Deactivate '+editing.code+'? Any current salary structure using it must be replaced with a new effective-dated structure before the next payroll. Existing posted payroll stays unchanged.'))return;
        busy=true;el('componentSave').disabled=true;root.inert=true;
        try {
            await api('/api/hr-payroll/salary-components'+(editing?'/'+editing.id:''),
                editing?'PUT':'POST',payload);
            editing=null;form.reset();el('componentActive').checked=true;el('componentType').value='1';
            el('componentFormTitle').textContent='New salary component';
            el('componentSave').textContent='Save component';guard?.markClean();
            if(await load())notice('Salary component saved.');
            else if(accessRevoked)notice('Salary component saved, but access to the register is no longer available. Sign in again.');
            else notice('Salary component saved; register refresh unavailable. Reload to verify.');
        }catch(e){handleError(e);if(!accessRevoked)notice(e.message+' Check the component register before retrying this change.',true);}
        finally{busy=false;el('componentSave').disabled=false;if(!accessRevoked)root.inert=false;}
    }
    el('componentForm').addEventListener('submit',save);
    el('componentClear').addEventListener('click',()=>clear());
    el('componentPresetTax').addEventListener('click',()=>preset('STAT_TAX','Income tax withholding'));
    el('componentPresetPf').addEventListener('click',()=>preset('STAT_PF','Provident fund deduction'));
    el('componentFilter').addEventListener('input',render);
    el('componentRefresh').addEventListener('click',()=>{if(!busy&&!accessRevoked)load();});
    load();
})();
