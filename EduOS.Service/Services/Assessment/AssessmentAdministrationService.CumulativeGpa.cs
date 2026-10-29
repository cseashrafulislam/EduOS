using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    /// <summary>
    /// Read only from eligible, frozen publication versions of the same tenant,
    /// programme and campus, never from today's editable marks. Repeated courses
    /// deliberately fail closed until an explicit improvement policy is approved.
    /// </summary>
    private async Task<string?> ApplyCumulativeGradePointsAsync(Assessment assessment,
        AcademicBatch batch, IReadOnlyList<(StudentEnrollment Enrollment, string Roll)> pupils,
        IReadOnlyList<AssessmentResultItemDto> results, CancellationToken token)
    {
        if (!assessment.CountsTowardCgpa) return null;
        if (_courseSnapshots == null)
            return "Course evidence storage is required for cumulative GPA.";
        var tenant = _user.TenantId;
        var resultByRoll = results.ToDictionary(x => x.RollNo);
        var studentByRoll = pupils.ToDictionary(x => x.Roll, x => x.Enrollment.StudentId);
        var allStudentIds = studentByRoll.Values.Distinct().ToArray();
        var priorByStudent = new Dictionary<long, List<(decimal GradePoints, decimal Credits, long[] CourseIds)>>();

        for (var offset = 0; offset < allStudentIds.Length; offset += 200)
        {
            var ids = allStudentIds.Skip(offset).Take(200).ToArray();
            var previous = await (
                from summary in _summaries.GetQueryable().AsNoTracking()
                join publication in _publications.GetQueryable().AsNoTracking()
                    on new { summary.TenantId, Id = summary.ResultPublicationId }
                    equals new { publication.TenantId, publication.Id }
                join historical in _assessments.GetQueryable().AsNoTracking()
                    on new { publication.TenantId, Id = publication.AssessmentId }
                    equals new { historical.TenantId, historical.Id }
                join enrollment in _enrollments.GetQueryable().AsNoTracking()
                    on new { summary.TenantId, Id = summary.StudentEnrollmentId }
                    equals new { enrollment.TenantId, enrollment.Id }
                where summary.TenantId == tenant && !summary.IsDeleted && !publication.IsDeleted &&
                    !historical.IsDeleted && !enrollment.IsDeleted &&
                    publication.State == ResultPublicationState.Published &&
                    historical.CountsTowardCgpa && historical.Id != assessment.Id &&
                    historical.EndDate < assessment.StartDate &&
                    historical.GradeSchemeId == assessment.GradeSchemeId &&
                    historical.CampusId == assessment.CampusId &&
                    enrollment.CampusId == assessment.CampusId &&
                    enrollment.AcademicProgramId == batch.AcademicProgramId &&
                    summary.PublicationVersionNo == publication.VersionNo &&
                    ids.Contains(enrollment.StudentId)
                select new
                {
                    enrollment.StudentId, enrollment.Id,
                    summary.ResultPublicationId, summary.GPA, summary.CreditHours,
                    summary.IsWithheld, publication.HasCourseGradeSnapshots
                }).Take(10001).ToListAsync(token);
            if (previous.Count > 10000)
                return "Cumulative result history exceeds the synchronous limit.";
            if (previous.Any(x => !x.HasCourseGradeSnapshots || x.IsWithheld ||
                !x.GPA.HasValue || !x.CreditHours.HasValue || x.CreditHours <= 0))
                return "Previous cumulative publication is incomplete or withheld; reconcile its immutable evidence.";
            var publicationIds = previous.Select(x => x.ResultPublicationId).Distinct().ToArray();
            var enrollmentIds = previous.Select(x => x.Id).Distinct().ToArray();
            var allCourses = await _courseSnapshots.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && publicationIds.Contains(x.ResultPublicationId) &&
                enrollmentIds.Contains(x.StudentEnrollmentId) && !x.IsDeleted)
                .Select(x => new { x.ResultPublicationId, x.StudentEnrollmentId,
                    x.CurriculumSubjectId, x.CreditHours, x.GradePoint, x.IsWithheld })
                .Take(25001).ToListAsync(token);
            if (allCourses.Count > 25000)
                return "Historical course evidence exceeds the synchronous limit.";
            var courseGroups = allCourses.GroupBy(x => (x.ResultPublicationId, x.StudentEnrollmentId))
                .ToDictionary(x => x.Key, x => x.ToArray());
            foreach (var earlier in previous)
            {
                if (!courseGroups.TryGetValue((earlier.ResultPublicationId, earlier.Id), out var courses) ||
                    courses.Length == 0 || courses.Any(x => x.CreditHours <= 0 || x.IsWithheld) ||
                    courses.Sum(x => x.CreditHours) != earlier.CreditHours ||
                    decimal.Round(courses.Sum(x => x.CreditHours * x.GradePoint) / earlier.CreditHours!.Value,
                        4, MidpointRounding.AwayFromZero) != earlier.GPA)
                    return "Historical GPA and course credit snapshots do not reconcile.";
                if (!priorByStudent.TryGetValue(earlier.StudentId, out var list))
                    priorByStudent[earlier.StudentId] = list = [];
                list.Add((earlier.GPA!.Value * earlier.CreditHours!.Value,
                    earlier.CreditHours.Value, courses.Select(x => x.CurriculumSubjectId).ToArray()));
            }
        }

        foreach (var (roll, studentId) in studentByRoll)
        {
            var result = resultByRoll[roll];
            if (!result.CreditHours.HasValue || result.CreditHours <= 0 ||
                !result.GradePointAverage.HasValue || result.Courses.Any(x => x.CreditHours <= 0))
                return "Cumulative grade credit evidence is incomplete.";
            var courseIds = result.Courses.Select(x => x.CurriculumSubjectId).ToArray();
            if (courseIds.Distinct().Count() != courseIds.Length)
                return "Duplicate curriculum courses are not eligible for cumulative credit.";
            decimal numerator = result.CreditHours.Value * result.GradePointAverage.Value;
            decimal denominator = result.CreditHours.Value;
            var seen = courseIds.ToHashSet();
            if (priorByStudent.TryGetValue(studentId, out var past))
            {
                foreach (var earlier in past)
                {
                    if (earlier.CourseIds.Any(x => !seen.Add(x)))
                        return "Repeated course needs an approved retake/improvement grade-selection policy.";
                    numerator += earlier.GradePoints;
                    denominator += earlier.Credits;
                }
            }
            result.CumulativeGradePointAverage = decimal.Round(numerator / denominator, 4,
                MidpointRounding.AwayFromZero);
        }
        return null;
    }
}
