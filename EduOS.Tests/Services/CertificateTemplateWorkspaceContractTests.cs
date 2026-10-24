using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CertificateTemplateWorkspaceContractTests
{
    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var file = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }

    [Fact]
    public void Template_register_is_tenant_scoped_paginated_and_excludes_deleted_rows()
    {
        var source = Read("EduOS.Service", "Services", "Exams", "ExamWorkflowService.CertificateTemplates.cs");
        source.Should().Contain("if (!CanPublish())");
        source.Should().Contain("x.TenantId == tenant && !x.IsDeleted");
        source.Should().Contain("Take(request.PageSize)");
        source.Should().Contain("locked.Contains(x.Id)");
        source.Should().Contain("RowVersion = Version(x.RowVersion)");
    }

    [Fact]
    public void Issued_templates_are_locked_and_edit_reads_full_template()
    {
        var source = Read("EduOS.Service", "Services", "Exams", "ExamWorkflowService.CertificateTemplates.cs");
        var write = Read("EduOS.Service", "Services", "Exams", "ExamWorkflowService.Documents.cs");
        source.Should().Contain("ids.Contains(x.CertificateTemplateId)");
        source.Should().Contain("MapTemplate(row)");
        write.Should().Contain("Issued certificate templates cannot be modified.");
        write.Should().Contain("!Matches(row.RowVersion, request.RowVersion)");
    }

    [Fact]
    public void Api_mvc_and_menu_use_privileged_exam_administration_roles()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "ExamWorkflowController.cs");
        var mvc = Read("EduOS.App", "Controllers", "ExamOperationsController.cs");
        var menu = Read("EduOS.App", "Views", "Shared", "_Layout.cshtml");
        api.Should().Contain("[HttpGet(\"certificate-templates\")]");
        api.Should().Contain("[HttpGet(\"certificate-templates/{id:long}\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        mvc.Should().Contain("public IActionResult CertificateTemplates() => View()");
        menu.Should().Contain("href=\"/ExamOperations/CertificateTemplates\"");
    }

    [Fact]
    public void Browser_supports_antiforgery_concurrency_safe_render_and_csv()
    {
        var js = Read("EduOS.App", "wwwroot", "js", "exams", "certificate-templates.js");
        var view = Read("EduOS.App", "Views", "ExamOperations", "CertificateTemplates.cshtml");
        js.Should().Contain("RequestVerificationToken: token");
        js.Should().Contain("rowVersion: id ? el('certificateTemplateVersion').value : null");
        js.Should().Contain("td.textContent");
        js.Should().Contain("row.isLocked || state.busy");
        js.Should().Contain("URL.createObjectURL");
        js.Should().Contain("replace(/");
        view.Should().Contain("@Html.AntiForgeryToken()");
        view.Should().Contain("id=\"certificateTemplateRows\"");
    }
}
