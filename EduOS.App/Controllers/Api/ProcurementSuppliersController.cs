using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
namespace EduOS.App.Controllers.Api;
[ApiController,Route("api/procurement/suppliers"),Authorize,RequireModule("INVENTORY"),AutoValidateAntiforgeryToken,EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ProcurementSuppliersController : ControllerBase
{
    private readonly IProcurementSupplierService service;
    public ProcurementSuppliersController(IProcurementSupplierService service)=>this.service=service;
    [HttpGet,Authorize(Roles="TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> List(int page=1,int pageSize=25,long? campusId=null,string? state=null,string? search=null,CancellationToken ct=default)=>Reply(await service.ListAsync(page,pageSize,campusId,state,search,ct));
    [HttpGet("{id:long}"),Authorize(Roles="TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Get(long id,CancellationToken ct)=>Reply(await service.GetAsync(id,ct));
    [HttpPost,Authorize(Roles="TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> Create([FromBody] SaveProcurementSupplierRequest request,CancellationToken ct)=>Reply(await service.SaveAsync(null,request,ct));
    [HttpPut("{id:long}"),Authorize(Roles="TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> Update(long id,[FromBody] SaveProcurementSupplierRequest request,CancellationToken ct)=>Reply(await service.SaveAsync(id,request,ct));
    [HttpPost("{id:long}/{action}"),Authorize(Roles="TenantAdmin,Principal,InventoryManager,StoreKeeper")]
    public async Task<IActionResult> Transition(long id,string action,[FromBody] ProcurementSupplierDecisionRequest request,CancellationToken ct)=>Reply(await service.TransitionAsync(id,action,request,ct));
    [HttpGet("export"),Authorize(Roles="TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant")]
    public async Task<IActionResult> Export(long? campusId=null,string? state=null,string? search=null,CancellationToken ct=default)
    {
        var result=await service.ListAsync(1,100,campusId,state,search,ct);
        if(!result.Success||result.Data==null)return StatusCode(result.StatusCode,result);
        if(result.Data.TotalCount>500)return StatusCode(413,new{message="Filter to 500 or fewer suppliers."});
        var rows=new List<ProcurementSupplierDto>(result.Data.Items);
        for(var page=2;page<=result.Data.TotalPages;page++){var next=await service.ListAsync(page,100,campusId,state,search,ct);if(!next.Success||next.Data==null)return StatusCode(next.StatusCode,next);rows.AddRange(next.Data.Items);}
        static string Csv(string? value){var s=value??"";if(s.TrimStart().Length>0&&"=+-@".Contains(s.TrimStart()[0]))s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";}
        var csv=new StringBuilder("Code,Name,Campus ID,Status,Email,Phone,Tax Registration\r\n");
        foreach(var x in rows)csv.AppendJoin(',',Csv(x.SupplierCode),Csv(x.Name),Csv(x.CampusId.ToString()),Csv(x.StateCode),Csv(x.Email),Csv(x.Phone),Csv(x.TaxRegistrationNo)).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()),"text/csv; charset=utf-8","procurement-suppliers.csv");
    }
    private IActionResult Reply<T>(ApiResponse<T> result)=>StatusCode(result.StatusCode,result);
}
