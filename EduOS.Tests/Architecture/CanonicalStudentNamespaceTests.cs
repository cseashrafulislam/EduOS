using EduOS.Core.DTOs.Students;
using FluentAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace EduOS.Tests.Architecture;

public sealed class CanonicalStudentNamespaceTests
{
    [Fact]
    public void Student_workflow_dtos_resolve_to_the_same_students_namespace()
    {
        Type[] contracts =
        [
            typeof(StudentDirectoryQueryDto), typeof(StudentDocumentDto),
            typeof(ProcessStudentExitDto), typeof(StudentLifecycleTimelineDto),
            typeof(StudentPhotoDto), typeof(PromoteStudentWorkflowRequestDto),
            typeof(StudentReadmissionOptionsDto), typeof(StudentTransferCertificateDto)
        ];
        contracts.Should().OnlyContain(t => t.Namespace == "EduOS.Core.DTOs.Students");
    }

    [Fact]
    public void Old_singular_dto_namespace_and_unused_student_service_cannot_return()
    {
        var root = FindRepositoryRoot();
        var oldDtoDirectory = Path.Combine(root, "EduOS.Core", "DTOs", "Student");
        if (Directory.Exists(oldDtoDirectory))
            Directory.EnumerateFiles(oldDtoDirectory, "*.cs", SearchOption.AllDirectories).Should().BeEmpty();
        File.Exists(Path.Combine(root, "EduOS.Service", "Services", "Students", "StudentService.cs"))
            .Should().BeFalse("the old commented-out service was never wired into DI");

        var oldNamespace = new Regex(@"EduOS\.Core\.DTOs\.Student(?=[.;])", RegexOptions.Compiled);
        foreach (var project in new[] { "EduOS.Core", "EduOS.Service", "EduOS.App" })
        {
            var sourceRoot = Path.Combine(root, project);
            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(x => x is "bin" or "obj")))
            {
                oldNamespace.IsMatch(File.ReadAllText(file)).Should().BeFalse($"{file} must use the canonical DTO namespace");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EduOS.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("EduOS solution root was not found.");
    }
}
