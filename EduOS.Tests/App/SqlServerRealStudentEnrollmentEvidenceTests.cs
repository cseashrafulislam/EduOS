using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerRealStudentEnrollmentEvidenceTests
{
    [Fact]
    public async Task Browser_student_section_offering_and_guardian_persist_once_without_cross_tenant_leakage()
    {
        var conn=Environment.GetEnvironmentVariable("EDUOS_SQL_REAL_QA_CONNECTION");
        var studentName=Environment.GetEnvironmentVariable("QA_STUDENT_NAME");
        var guardianName=Environment.GetEnvironmentVariable("QA_GUARDIAN_NAME");
        var batchName=Environment.GetEnvironmentVariable("QA_BATCH_NAME");
        var curriculumName=Environment.GetEnvironmentVariable("QA_CURRICULUM_NAME");
        var roll=Environment.GetEnvironmentVariable("QA_STUDENT_ROLL");
        if(new[]{conn,studentName,guardianName,batchName,curriculumName,roll}
            .All(string.IsNullOrWhiteSpace)) return;
        Assert.False(string.IsNullOrWhiteSpace(conn));
        Assert.StartsWith("QA Student ",studentName);
        Assert.StartsWith("QA Guardian ",guardianName);
        Assert.StartsWith("QA Batch ",batchName);
        Assert.StartsWith("QA Curriculum ",curriculumName);
        Assert.StartsWith("QA-Roll-",roll);
        // Only local disposable SQL targets; reject remote database before connecting.
        var target=new SqlConnectionStringBuilder(conn);
        Assert.Equal("127.0.0.1,14333",target.DataSource);
        Assert.Contains(target.InitialCatalog,new[]{"EduOS_Demo_AutoQa","EduOS_Demo_Codespaces"});
        var options=new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(conn).Options;
        await using var db=new EduOSDbContext(options);
        Assert.Equal(target.InitialCatalog,db.Database.GetDbConnection().Database);
        var tenant=await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x=>x.Code=="EDUOS-DEMO"&&!x.IsDeleted);
        var student=await db.Students.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x=>x.TenantId==tenant.Id&&x.FullName==studentName&&!x.IsDeleted);
        var enrollment=await db.StudentEnrollments.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x=>x.TenantId==tenant.Id&&x.StudentId==student.Id&&!x.IsDeleted);
        var batch=await db.AcademicBatches.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x=>x.TenantId==tenant.Id&&x.Name==batchName&&!x.IsDeleted);
        var curriculum=await db.AcademicCurriculums.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x=>x.TenantId==tenant.Id&&x.Name==curriculumName&&!x.IsDeleted);
        var mapping=await db.CurriculumSubjects.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x=>x.TenantId==tenant.Id&&x.AcademicCurriculumId==curriculum.Id&&
                x.AcademicLevelId==batch.AcademicLevelId&&!x.IsDeleted);
        var legacyOffering=await db.SubjectOfferings.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x=>x.TenantId==tenant.Id&&x.AcademicBatchId==batch.Id&&
                x.CurriculumSubjectId==mapping.Id&&!x.IsDeleted);
        var sectionOffering=await db.SubjectOfferings.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x=>x.TenantId==tenant.Id&&x.AcademicBatchId==null&&
                x.AcademicYearId==batch.AcademicYearId&&
                x.AcademicSectionId==enrollment.AcademicSectionId&&
                x.CurriculumSubjectId==mapping.Id&&!x.IsDeleted);
        var registration=await db.StudentSubjectRegistrations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x=>x.TenantId==tenant.Id&&x.StudentEnrollmentId==enrollment.Id&&!x.IsDeleted);
        var link=await db.StudentGuardians.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x=>x.TenantId==tenant.Id&&x.StudentId==student.Id&&!x.IsDeleted);
        var guardian=await db.Guardians.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x=>x.TenantId==tenant.Id&&x.Id==link.GuardianId&&!x.IsDeleted);
        Assert.StartsWith("STU-",student.StudentCode);
        Assert.Equal(student.Id,enrollment.StudentId);
        Assert.Null(enrollment.AcademicBatchId);
        Assert.Equal(batch.AcademicYearId,enrollment.AcademicYearId);
        Assert.Equal(curriculum.Id,enrollment.AcademicCurriculumId);
        Assert.Equal(sectionOffering.AcademicSectionId,enrollment.AcademicSectionId);
        Assert.Equal(legacyOffering.AcademicSectionId,enrollment.AcademicSectionId);
        Assert.Equal(roll,enrollment.RollNo);
        Assert.True(enrollment.IsCurrent);
        Assert.True(curriculum.IsCurrent);
        Assert.True(mapping.IsActive&&!mapping.IsOptional);
        Assert.True(legacyOffering.IsActive);
        Assert.True(sectionOffering.IsActive);
        Assert.Equal(sectionOffering.Id,registration.SubjectOfferingId);
        Assert.NotEqual(legacyOffering.Id,registration.SubjectOfferingId);
        Assert.Equal(guardianName,guardian.FullName);
        Assert.True(link.IsPrimary);
        Assert.Equal(new DateOnly(2027,3,1),enrollment.EnrollmentDate);
        Assert.Equal(25,batch.Capacity);
        Assert.Equal(20,legacyOffering.Capacity);
        Assert.Equal(20,sectionOffering.Capacity);
        // The UI replay and changed-payload 409 checks must not create
        // second Student, Person, Enrollment, Guardian or subject reservation.
        Assert.Equal(1,await db.StudentEnrollments.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x=>x.TenantId==tenant.Id&&x.ClientRequestId==enrollment.ClientRequestId));
        Assert.False(await db.Students.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x=>x.TenantId!=tenant.Id&&x.FullName==studentName));
    }
}
