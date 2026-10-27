using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Interfaces.IServices;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AcademicInstructionUiContractTests
{
    [Fact]
    public void Canonical_lesson_and_substitution_requests_have_the_fields_used_by_the_workspace()
    {
        typeof(SaveLessonPlanRequestDto).GetProperty(nameof(SaveLessonPlanRequestDto.SubjectOfferingReference))
            .Should().NotBeNull();
        typeof(SaveLessonPlanRequestDto).GetProperty(nameof(SaveLessonPlanRequestDto.LessonDate))
            .Should().NotBeNull();
        typeof(CreateSubstitutionRequestDto).GetProperty(nameof(CreateSubstitutionRequestDto.SubstituteEmployeeReference))
            .Should().NotBeNull();
        typeof(CancelSubstitutionRequestDto).GetProperty(nameof(CancelSubstitutionRequestDto.RowVersion))
            .Should().NotBeNull();
        typeof(IAcademicInstructionService).GetMethod(nameof(IAcademicInstructionService.GetLessonPlansAsync))
            .Should().NotBeNull();
    }

    [Fact]
    public void Instruction_mutations_require_authorization_and_antiforgery()
    {
        var type = typeof(AcademicInstructionController);
        type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Should().NotBeEmpty();
        type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true).Should().NotBeEmpty();
        foreach (var name in new[] { nameof(AcademicInstructionController.CreateSubstitution),
            nameof(AcademicInstructionController.CancelSubstitution),
            nameof(AcademicInstructionController.ReviewLessonPlan) })
        {
            type.GetMethod(name)!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>().Single().Roles.Should().Contain("Principal");
        }
    }

    [Fact]
    public void Instruction_workspace_uses_paged_lesson_results_and_canonical_write_payloads()
    {
        var js = File.ReadAllText(FindFile("EduOS.App", "wwwroot", "js", "academic", "instruction.js"));
        var view = File.ReadAllText(FindFile("EduOS.App", "Views", "AcademicInstruction", "Index.cshtml"));
        js.Should().Contain("lessonResult.value");
        js.Should().Contain("page.items");
        js.Should().Contain("subjectOfferingReference: offering");
        js.Should().Contain("substituteEmployeeReference");
        js.Should().Contain("rowVersion: row.rowVersion");
        js.Should().Contain("headers.RequestVerificationToken = token");
        js.Should().Contain("state.lessonPage");
        js.Should().Contain("/api/academic-instruction/batches/");
        js.Should().Contain("/lesson-offerings?");
        js.Should().NotContain("/api/academic-setup/batches/");
        js.Should().NotContain("chapterName:");
        js.Should().NotContain("progressPercent");
        js.Should().NotContain("substituteTeacherId");
        view.Should().Contain("id=\"lessonPrevious\"");
        view.Should().Contain("id=\"lessonNext\"");
        view.Should().Contain("id=\"lessonCsv\"");
        view.Should().Contain("id=\"substitutionCsv\"");
        js.Should().Contain("function exportSubstitutions()");
        js.Should().Contain("row.originalTeacherName");
        view.Should().Contain("id=\"substitutionTeacherFind\"");
    }

    private static string FindFile(params string[] segments)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }
}
