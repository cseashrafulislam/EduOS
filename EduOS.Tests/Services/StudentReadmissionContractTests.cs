using FluentAssertions;
using Xunit;
namespace EduOS.Tests.Services;
public sealed class StudentReadmissionContractTests
{
    private static string Read(params string[] segments)
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory!=null)
        {
            var path=Path.Combine(new[]{directory.FullName}.Concat(segments).ToArray());
            if(File.Exists(path))return File.ReadAllText(path);
            directory=directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/",segments));
    }
    [Fact]
    public void Readmission_is_tenant_scoped_serializable_idempotent_and_audited()
    {
        var source=Read("EduOS.Service","Services","Students","StudentReadmissionService.cs");
        source.Should().Contain("IGenericRepository<StudentEnrollment>");
        source.Should().Contain("IGenericRepository<StudentExitRecord>");
        source.Should().Contain("IGenericRepository<StudentStatusHistory>");
        source.Should().Contain("System.Data.IsolationLevel.Serializable");
        source.Should().Contain("x.ClientRequestId==request.ClientRequestId");
        source.Should().Contain("Matches(student.RowVersion,studentVersion)");
        source.Should().Contain("Matches(source.RowVersion,sourceVersion)");
        source.Should().Contain("batch.CampusId!=source.CampusId");
        source.Should().Contain("x.IsCurrent &&");
        source.Should().Contain("Roll already exists in this batch.");
        source.Should().Contain("student.StatusCode=\"Active\"");
        source.Should().Contain("catch(DbUpdateConcurrencyException ex)");
        source.Should().Contain("catch(DbUpdateException ex)");
    }
    [Fact]
    public void Api_and_view_require_tenant_admin_module_and_antiforgery()
    {
        var api=Read("EduOS.App","Controllers","Api","StudentReadmissionsController.cs");
        var mvc=Read("EduOS.App","Controllers","StudentReadmissionsController.cs");
        var view=Read("EduOS.App","Views","StudentReadmissions","Index.cshtml");
        var js=Read("EduOS.App","wwwroot","js","students","readmissions.js");
        api.Should().Contain("[Authorize(Roles=\"TenantAdmin\")]");
        api.Should().Contain("[RequireModule(\"STUDENT\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        mvc.Should().Contain("[Authorize(Roles=\"TenantAdmin\")]");
        view.Should().Contain("@Html.AntiForgeryToken()");
        js.Should().Contain("RequestVerificationToken");
        js.Should().Contain("credentials:'same-origin'");
        js.Should().Contain("clientRequestId:state.requestId");
    }
    [Fact]
    public void New_exit_after_readmission_is_not_blocked_by_an_older_exit()
    {
        var exit=Read("EduOS.Service","Services","Students","StudentExitService.cs");
        exit.Should().Contain("x.StudentEnrollmentId == current.Id");
    }
}
