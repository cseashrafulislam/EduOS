using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Mail;
using System.Text.RegularExpressions;
namespace EduOS.Service.Services.Inventory;
public sealed class ProcurementSupplierService : IProcurementSupplierService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    private readonly ILogger<ProcurementSupplierService> logger;
    public ProcurementSupplierService(EduOSDbContext db,ICurrentUserService user,TimeProvider clock,ILogger<ProcurementSupplierService> logger)
    {this.db=db;this.user=user;this.clock=clock;this.logger=logger;}
    private bool Reader=>user.IsAuthenticated&&user.TenantId>0&&(user.IsTenantAdmin||user.IsInRole("Principal")||user.IsInRole("InventoryManager")||user.IsInRole("StoreKeeper")||user.IsInRole("Accountant"));
    private bool Maker=>Reader&&(user.IsTenantAdmin||user.IsInRole("Principal")||user.IsInRole("InventoryManager")||user.IsInRole("StoreKeeper"));
    private bool Checker=>Reader&&(user.IsTenantAdmin||user.IsInRole("Principal")||user.IsInRole("InventoryManager"));
    private bool AllCampuses=>user.IsTenantAdmin||user.IsInRole("Principal");
    private static ApiResponse<T> Denied<T>()=>ApiResponse<T>.ErrorResponse("Supplier access denied.",403);
    private IQueryable<ProcurementSupplier> Visible()=>db.ProcurementSuppliers.AsNoTracking().Where(x=>x.TenantId==user.TenantId&&!x.IsDeleted&&db.Campuses.Any(c=>c.TenantId==user.TenantId&&c.Id==x.CampusId&&!c.IsDeleted)&&(AllCampuses||db.UserCampusAccesses.Any(a=>a.TenantId==user.TenantId&&a.UserId==user.UserId&&a.CampusId==x.CampusId&&a.IsActive&&!a.IsDeleted)));
    private async Task<bool> CampusAllowed(long id,CancellationToken ct)=>id>0&&await db.Campuses.AsNoTracking().AnyAsync(c=>c.TenantId==user.TenantId&&c.Id==id&&c.IsActive&&!c.IsDeleted,ct)&&(AllCampuses||await db.UserCampusAccesses.AsNoTracking().AnyAsync(a=>a.TenantId==user.TenantId&&a.CampusId==id&&a.UserId==user.UserId&&a.IsActive&&!a.IsDeleted,ct));
    private static bool Version(byte[] current,string? supplied){try{return !string.IsNullOrWhiteSpace(supplied)&&current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied));}catch(FormatException){return false;}}
    private static string? Validate(SaveProcurementSupplierRequest? r)
    {
        if(r==null||r.ClientRequestId==Guid.Empty||r.CampusId<=0||string.IsNullOrWhiteSpace(r.SupplierCode)||!Regex.IsMatch(r.SupplierCode.Trim(),"^[A-Za-z0-9][A-Za-z0-9_-]{1,39}$")||string.IsNullOrWhiteSpace(r.Name)||r.Name.Trim().Length is <3 or >200||r.Email?.Trim().Length>200||r.Phone?.Trim().Length>40||r.TaxRegistrationNo?.Trim().Length>100)return "Invalid supplier details.";
        if(!string.IsNullOrWhiteSpace(r.Email)&&(!MailAddress.TryCreate(r.Email.Trim(),out var address)||!string.Equals(address.Address,r.Email.Trim(),StringComparison.OrdinalIgnoreCase)))return "Invalid email.";
        return null;
    }
    private static ProcurementSupplierDto Map(ProcurementSupplier x)=>new(){Id=x.Id,ClientRequestId=x.ClientRequestId,CampusId=x.CampusId,SupplierCode=x.SupplierCode,Name=x.Name,Email=x.Email,Phone=x.Phone,TaxRegistrationNo=x.TaxRegistrationNo,StateCode=x.StateCode,CreatedBy=x.CreatedBy,CreatedAt=x.CreatedAt,SubmittedAt=x.SubmittedAt,DecidedAt=x.DecidedAt,DecidedByUserId=x.DecidedByUserId,DecisionReason=x.DecisionReason,RowVersion=Convert.ToBase64String(x.RowVersion)};
    public async Task<ApiResponse<PagedResult<ProcurementSupplierDto>>> ListAsync(int page,int pageSize,long? campusId,string? state,string? search,CancellationToken ct=default)
    {
        if(!Reader)return Denied<PagedResult<ProcurementSupplierDto>>();
        if(page<1||pageSize is <1 or >100||(long)(page-1)*pageSize>int.MaxValue||campusId is <=0||search?.Length>100||state?.Length>20)return ApiResponse<PagedResult<ProcurementSupplierDto>>.ErrorResponse("Invalid filters.");
        var query=Visible();
        if(campusId.HasValue)query=query.Where(x=>x.CampusId==campusId);
        if(!string.IsNullOrWhiteSpace(state))query=query.Where(x=>x.StateCode==state);
        if(!string.IsNullOrWhiteSpace(search)){var term=search.Trim();query=query.Where(x=>x.SupplierCode.Contains(term)||x.Name.Contains(term));}
        var total=await query.CountAsync(ct);
        var rows=await query.OrderBy(x=>x.SupplierCode).ThenBy(x=>x.Id).Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<ProcurementSupplierDto>>.SuccessResponse(new PagedResult<ProcurementSupplierDto>{Page=page,PageSize=pageSize,TotalCount=total,Items=rows.Select(Map).ToList()});
    }
    public async Task<ApiResponse<ProcurementSupplierDto>> GetAsync(long id,CancellationToken ct=default)
    {
        if(!Reader)return Denied<ProcurementSupplierDto>();
        var row=await Visible().FirstOrDefaultAsync(x=>x.Id==id,ct);
        return row==null?ApiResponse<ProcurementSupplierDto>.ErrorResponse("Supplier not found.",404):ApiResponse<ProcurementSupplierDto>.SuccessResponse(Map(row));
    }
    public async Task<ApiResponse<ProcurementSupplierDto>> SaveAsync(long? id,SaveProcurementSupplierRequest request,CancellationToken ct=default)
    {
        if(!Maker)return Denied<ProcurementSupplierDto>();
        var error=Validate(request);
        if(error!=null||id is <=0)return ApiResponse<ProcurementSupplierDto>.ErrorResponse(error??"Invalid ID.");
        if(!await CampusAllowed(request.CampusId,ct))return Denied<ProcurementSupplierDto>();
        var code=request.SupplierCode.Trim().ToUpperInvariant();var name=request.Name.Trim();
        var email=string.IsNullOrWhiteSpace(request.Email)?null:request.Email.Trim();
        var phone=string.IsNullOrWhiteSpace(request.Phone)?null:request.Phone.Trim();
        var tax=string.IsNullOrWhiteSpace(request.TaxRegistrationNo)?null:request.TaxRegistrationNo.Trim();
        ProcurementSupplier row;
        if(id.HasValue)
        {
            var found=await db.ProcurementSuppliers.FirstOrDefaultAsync(x=>x.TenantId==user.TenantId&&x.Id==id.Value&&!x.IsDeleted,ct);
            if(found==null||!await CampusAllowed(found.CampusId,ct))return ApiResponse<ProcurementSupplierDto>.ErrorResponse("Supplier not found.",404);
            if(found.CreatedBy!=user.UserId||found.StateCode!="Draft"||found.CampusId!=request.CampusId||found.ClientRequestId!=request.ClientRequestId)return ApiResponse<ProcurementSupplierDto>.ErrorResponse("Only maker may edit draft.",409);
            if(!Version(found.RowVersion,request.RowVersion))return ApiResponse<ProcurementSupplierDto>.ErrorResponse("Supplier changed; reload.",409);
            row=found;
        }
        else
        {
            var prior=await db.ProcurementSuppliers.AsNoTracking().FirstOrDefaultAsync(x=>x.TenantId==user.TenantId&&x.ClientRequestId==request.ClientRequestId,ct);
            if(prior!=null)
            {
                if(prior.CreatedBy!=user.UserId||!await CampusAllowed(prior.CampusId,ct))return Denied<ProcurementSupplierDto>();
                return prior.CampusId==request.CampusId&&prior.SupplierCode==code&&prior.Name==name&&prior.Email==email&&prior.Phone==phone&&prior.TaxRegistrationNo==tax?ApiResponse<ProcurementSupplierDto>.SuccessResponse(Map(prior),"Existing request."):ApiResponse<ProcurementSupplierDto>.ErrorResponse("Request ID conflict.",409);
            }
            row=new ProcurementSupplier{TenantId=user.TenantId,CampusId=request.CampusId,ClientRequestId=request.ClientRequestId,CreatedAt=clock.GetUtcNow().UtcDateTime,CreatedBy=user.UserId};
            db.ProcurementSuppliers.Add(row);
        }
        row.SupplierCode=code;row.Name=name;row.Email=email;row.Phone=phone;row.TaxRegistrationNo=tax;
        row.UpdatedAt=clock.GetUtcNow().UtcDateTime;row.UpdatedBy=user.UserId;
        try{await db.SaveChangesAsync(ct);return ApiResponse<ProcurementSupplierDto>.SuccessResponse(Map(row));}
        catch(DbUpdateConcurrencyException){return ApiResponse<ProcurementSupplierDto>.ErrorResponse("Supplier changed; reload.",409);}
        catch(DbUpdateException ex){logger.LogWarning(ex,"Supplier integrity conflict for tenant {TenantId}",user.TenantId);return ApiResponse<ProcurementSupplierDto>.ErrorResponse("Duplicate supplier code/request.",409);}
    }
    public async Task<ApiResponse<ProcurementSupplierDto>> TransitionAsync(long id,string action,ProcurementSupplierDecisionRequest request,CancellationToken ct=default)
    {
        if(!Reader)return Denied<ProcurementSupplierDto>();
        if(id<=0||request==null||action is not ("submit" or "approve" or "reject" or "revise" or "deactivate")||request.Reason?.Length>500)return ApiResponse<ProcurementSupplierDto>.ErrorResponse("Invalid decision.");
        var row=await db.ProcurementSuppliers.FirstOrDefaultAsync(x=>x.TenantId==user.TenantId&&x.Id==id&&!x.IsDeleted,ct);
        if(row==null||!await CampusAllowed(row.CampusId,ct))return ApiResponse<ProcurementSupplierDto>.ErrorResponse("Supplier not found.",404);
        var owner=row.CreatedBy==user.UserId;
        if((action is "submit" or "revise"&&(!Maker||!owner))||(action is "approve" or "reject" or "deactivate"&&(!Checker||owner)))return Denied<ProcurementSupplierDto>();
        if(!Version(row.RowVersion,request.RowVersion))return ApiResponse<ProcurementSupplierDto>.ErrorResponse("Supplier changed; reload.",409);
        var valid=action switch{"submit"=>row.StateCode=="Draft","approve" or "reject"=>row.StateCode=="Submitted","revise"=>row.StateCode=="Rejected","deactivate"=>row.StateCode=="Approved",_=>false};
        if(!valid)return ApiResponse<ProcurementSupplierDto>.ErrorResponse("Invalid transition.",409);
        if(action is "reject" or "deactivate"&&string.IsNullOrWhiteSpace(request.Reason))return ApiResponse<ProcurementSupplierDto>.ErrorResponse("Reason required.");
        row.StateCode=action switch{"submit"=>"Submitted","approve"=>"Approved","reject"=>"Rejected","revise"=>"Draft","deactivate"=>"Inactive",_=>row.StateCode};
        var now=clock.GetUtcNow().UtcDateTime;
        if(action=="submit"){row.SubmittedAt=now;row.DecidedAt=null;row.DecidedByUserId=null;row.DecisionReason=null;}
        if(action=="revise"){row.SubmittedAt=null;row.DecidedAt=null;row.DecidedByUserId=null;row.DecisionReason=null;}
        if(action is "approve" or "reject" or "deactivate"){row.DecidedAt=now;row.DecidedByUserId=user.UserId;row.DecisionReason=request.Reason?.Trim();}
        row.UpdatedAt=now;row.UpdatedBy=user.UserId;
        try{await db.SaveChangesAsync(ct);return ApiResponse<ProcurementSupplierDto>.SuccessResponse(Map(row));}
        catch(DbUpdateConcurrencyException){return ApiResponse<ProcurementSupplierDto>.ErrorResponse("Competing decision; reload.",409);}
    }
}
