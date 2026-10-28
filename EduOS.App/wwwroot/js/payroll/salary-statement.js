(() => {
    'use strict';
    const root=document.getElementById('payrollSalaryStatement');if(!root)return;
    const el=id=>document.getElementById(id);
    const status={1:'Draft',2:'Calculated',3:'Approved',4:'Posted',5:'Cancelled'};
    const money=n=>Number(n||0).toLocaleString('en-BD',{minimumFractionDigits:2,maximumFractionDigits:2});
    let page=1,pages=1;
    const today=new Date();el('statementYear').value=today.getFullYear();
    el('statementMonth').value=String(today.getMonth()+1);
    function error(text) {const box=el('statementError');box.className='alert alert-danger';box.textContent=text;box.focus();}
    async function get(url) {
        const response=await fetch(url,{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
        const result=await response.json().catch(()=>null);
        if(!response.ok||!result?.success)throw Error(result?.message||'Unable to load report.');
        return result.data;
    }
    const period=()=>{const year=Number(el('statementYear').value),month=Number(el('statementMonth').value);
        if(!Number.isInteger(year)||year<2000||year>2200||!Number.isInteger(month)||month<1||month>12)
            throw Error('Choose a valid year and month.');
        return {year,month};
    };
    const td=(value,right=false)=>{const x=document.createElement('td');x.textContent=String(value??'');if(right)x.className='text-end';return x;};
    async function load() {
        try {
            const query=new URLSearchParams({...period(),page:String(page),pageSize:'25'});
            const data=await get('/api/payroll-salary-statement?'+query);
            const body=el('statementRows');body.replaceChildren();
            let gross=0,deductions=0,net=0;
            for(const item of data.items||[]) {
                gross+=Number(item.grossAmount);deductions+=Number(item.deductionAmount);net+=Number(item.netAmount);
                const row=document.createElement('tr');
                [item.runNumber,item.employeeCode+' · '+item.employeeName,
                    String(item.periodStart).slice(0,10)+' – '+String(item.periodEnd).slice(0,10),
                    status[item.state]||item.state].forEach(x=>row.append(td(x)));
                [item.grossAmount,item.deductionAmount,item.netAmount].forEach(x=>row.append(td(money(x),true)));
                body.append(row);
            }
            if(!data.items?.length) {const row=document.createElement('tr'),cell=td('No salary rows.');cell.colSpan=7;row.append(cell);body.append(row);}
            el('statementGross').textContent=money(gross);el('statementDeduction').textContent=money(deductions);
            el('statementNet').textContent=money(net);
            pages=Math.max(1,Math.ceil((data.totalCount||0)/25));page=Number(data.page)||page;
            el('statementPage').textContent='Page '+page+' of '+pages+' · '+(data.totalCount||0)+' rows';
            el('statementPrev').disabled=page<=1;el('statementNext').disabled=page>=pages;
            el('statementError').className='d-none';
        }catch(e){error(e.message);}
    }
    el('statementFilter').addEventListener('submit',e=>{e.preventDefault();page=1;load();});
    el('statementPrev').addEventListener('click',()=>{page=Math.max(1,page-1);load();});
    el('statementNext').addEventListener('click',()=>{page=Math.min(pages,page+1);load();});
    el('statementCsv').addEventListener('click',()=>{
        try {const query=new URLSearchParams(period());window.location.assign('/api/payroll-salary-statement/csv?'+query);}
        catch(e){error(e.message);}
    });
    el('statementPrint').addEventListener('click',()=>window.print());
    load();
})();
