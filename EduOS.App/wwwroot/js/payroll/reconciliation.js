(() => {
    'use strict';
    const root=document.getElementById('payrollReconciliation');if(!root)return;
    const el=id=>document.getElementById(id),money=v=>Number(v||0).toLocaleString('en-BD',
        {minimumFractionDigits:2,maximumFractionDigits:2});
    const now=new Date();el('payrollReconYear').value=now.getFullYear();
    el('payrollReconMonth').value=String(now.getMonth()+1);
    let page=1,pages=1,generation=0,loading=false;
    const td=(text,right=false)=>{const c=document.createElement('td');c.textContent=String(text??'');
        if(right)c.className='text-end';return c;};
    const reportNotice=(message)=>{const x=el('payrollReconNotice');x.className='alert alert-danger';x.textContent=message;x.focus();};
    const period=()=>{
        const year=Number(el('payrollReconYear').value),month=Number(el('payrollReconMonth').value);
        if(!Number.isInteger(year)||year<2000||year>2200||!Number.isInteger(month)||month<1||month>12)
            throw Error('Select a valid payroll year and month.');
        const q={year,month};const c=el('payrollReconCampus').value;
        if(c)q.campusId=c;return q;
    };
    async function get(url){
        const response=await fetch(url,{credentials:'same-origin',cache:'no-store',
            headers:{Accept:'application/json'}});
        const data=await response.json().catch(()=>null);
        if(!response.ok||!data?.success)throw Error(data?.message||'Payroll reconciliation failed.');
        return data.data;
    }
    async function load(){
        const turn=++generation;loading=true;
        try{
            const q=new URLSearchParams({...period(),page:String(page),pageSize:'25'});
            const data=await get('/api/finance/payroll-reconciliation?'+q);
            if(turn!==generation)return;
            const body=el('payrollReconRows');body.replaceChildren();
            const totals=[0,0,0,0];
            for(const r of data.items||[]){
                const campus=r.campusId
                    ?el('payrollReconCampus').querySelector('option[value="'+r.campusId+'"]')?.textContent||('Campus '+r.campusId)
                    :'Institution-wide';
                const row=document.createElement('tr');
                [r.runNumber,(r.employeeCode||'')+' · '+(r.employeeName||''),campus].forEach(v=>row.append(td(v)));
                [r.netPayable,r.verifiedPaid,r.pendingVerification,r.outstanding].forEach((v,i)=>{
                    totals[i]+=Number(v||0);row.append(td(money(v),true));
                });
                [r.accrualStatus,r.settlementStatus].forEach(v=>row.append(td(v)));
                body.append(row);
            }
            if(!data.items?.length){const row=document.createElement('tr'),c=td('No payroll results.');
                c.colSpan=9;row.append(c);body.append(row);}
            ['Net','Paid','Pending','Due'].forEach((n,i)=>el('payrollReconTotal'+n).textContent=money(totals[i]));
            pages=Math.max(1,Math.ceil((data.totalCount||0)/25));
            page=Number(data.page)||page;
            el('payrollReconPage').textContent='Page '+page+' of '+pages+' · '+(data.totalCount||0)+' employees';
            el('payrollReconPrev').disabled=page<=1;el('payrollReconNext').disabled=page>=pages;
            el('payrollReconNotice').className='d-none';
        }catch(e){if(turn===generation)reportNotice(e.message);}
        finally{if(turn===generation)loading=false;}
    }
    el('payrollReconFilter').addEventListener('submit',e=>{e.preventDefault();page=1;load();});
    el('payrollReconPrev').addEventListener('click',()=>{if(!loading){page=Math.max(1,page-1);load();}});
    el('payrollReconNext').addEventListener('click',()=>{if(!loading){page=Math.min(pages,page+1);load();}});
    el('payrollReconExport').addEventListener('click',()=>{
        try{window.location.assign('/api/finance/payroll-reconciliation/csv?'+new URLSearchParams(period()));}
        catch(e){reportNotice(e.message);}
    });
    el('payrollReconPrint').addEventListener('click',()=>window.print());
    get('/api/institution-onboarding/campus-list').then(campuses=>{
        const s=el('payrollReconCampus');
        for(const c of campuses||[])if(c.isActive)s.add(new Option(c.name,c.id));
    }).catch(e=>reportNotice('Campus list: '+e.message));
    load();
})();