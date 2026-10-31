/*
 EduOS DEMONSTRATION DATA ONLY — run in SSMS against a newly migrated
 SQL Server database whose name begins EduOS_Demo_.

 FIRST: start EduOS once in Staging with DemoData:Enabled=true and a strong
 DemoData:SharedPassword. That creates an isolated demo institution and 13
 Identity-hashed demo role users. This script then seeds 12 realistic but
 FICTIONAL rows per supported academic/student master and relationship table.

 NO real names, identifiers, bank accounts, payments, posted transactions,
 audit events, session tokens, consent, grades or approval evidence are made up.
 Other operational tables must be populated through their real app workflows.

 Idempotent for the designated demo codes; repeat runs do not reset any data.
 All changes commit together or roll back together. Never run on live data.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF LEFT(DB_NAME(), 11) <> N'EduOS_Demo_'
    THROW 52001, 'REFUSED: select a dedicated EduOS_Demo_* database first.', 1;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL OR
   OBJECT_ID(N'dbo.Tenants', N'U') IS NULL OR
   OBJECT_ID(N'dbo.StudentEnrollments', N'U') IS NULL
    THROW 52002, 'REFUSED: EduOS migrations are not fully applied.', 1;

IF EXISTS (SELECT 1 FROM dbo.Tenants
           WHERE Code NOT IN (N'EDUOS-DEMO', N'EDUOS-SYSTEM') AND IsDeleted = 0)
    THROW 52003, 'REFUSED: this database contains another institution.', 1;

DECLARE @TenantId bigint = (SELECT Id FROM dbo.Tenants
                             WHERE Code = N'EDUOS-DEMO' AND State = 2 AND IsDeleted = 0);
IF @TenantId IS NULL
    THROW 52004, 'REFUSED: run the demo Identity bootstrap first.', 1;

DECLARE @StudentUserId bigint = (SELECT Id FROM dbo.AspNetUsers
                                 WHERE NormalizedEmail = N'DEMO.STUDENT@EDUOS.EXAMPLE.INVALID');
DECLARE @ParentUserId bigint = (SELECT Id FROM dbo.AspNetUsers
                                WHERE NormalizedEmail = N'DEMO.PARENT@EDUOS.EXAMPLE.INVALID');
IF @StudentUserId IS NULL OR @ParentUserId IS NULL
    THROW 52005, 'REFUSED: role users are missing; run demo bootstrap first.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @i int = 1;
    DECLARE @s char(2), @yearCode nvarchar(30), @campusCode nvarchar(30),
            @departmentCode nvarchar(30), @programCode nvarchar(30),
            @levelCode nvarchar(30), @termCode nvarchar(30),
            @trackCode nvarchar(30), @mediumCode nvarchar(30),
            @shiftCode nvarchar(30), @batchCode nvarchar(30),
            @subjectCode nvarchar(30), @curriculumCode nvarchar(30),
            @studentCode nvarchar(30), @guardianName nvarchar(200),
            @studentName nvarchar(200), @campusId bigint, @yearId bigint,
            @termId bigint, @departmentId bigint, @programId bigint,
            @levelId bigint, @trackId bigint, @mediumId bigint,
            @shiftId bigint, @batchId bigint, @subjectId bigint,
            @curriculumId bigint, @curriculumSubjectId bigint,
            @studentId bigint, @guardianId bigint, @personId bigint,
            @guardianPersonId bigint, @enrollmentId bigint, @offeringId bigint;

    WHILE @i <= 12
    BEGIN
        SET @s = RIGHT('00' + CONVERT(varchar(2), @i), 2);
        SET @campusCode = N'DEMO-C' + @s;
        SET @yearCode = N'DEMO-Y' + @s;
        SET @termCode = N'DEMO-T' + @s;
        SET @departmentCode = N'DEMO-D' + @s;
        SET @programCode = N'DEMO-P' + @s;
        SET @levelCode = N'DEMO-L' + @s;
        SET @trackCode = N'DEMO-TR' + @s;
        SET @mediumCode = N'DEMO-M' + @s;
        SET @shiftCode = N'DEMO-SH' + @s;
        SET @batchCode = N'DEMO-B' + @s;
        SET @subjectCode = N'DEMO-SUB' + @s;
        SET @curriculumCode = N'DEMO-CUR' + @s;
        SET @studentCode = N'DEMO-ST' + @s;

        SET @studentName = CHOOSE(@i,
            N'Arif Hasan', N'Nusrat Jahan', N'Tanvir Rahman', N'Samia Akter',
            N'Rafiul Islam', N'Mehjabin Chowdhury', N'Sadman Hossain', N'Farzana Islam',
            N'Raihan Ahmed', N'Anika Sultana', N'Mahmudul Hasan', N'Tasnim Rahman');
        SET @guardianName = CHOOSE(@i,
            N'Mohammad Karim', N'Salma Begum', N'Kamal Uddin', N'Nasima Khatun',
            N'Abdul Hakim', N'Rumana Akter', N'Jahangir Alam', N'Farida Yasmin',
            N'Jamal Hossain', N'Roksana Parvin', N'Khaled Rahman', N'Shahana Begum');

        -- Campuses: first one exists from Identity bootstrap; eleven are added.
        IF NOT EXISTS (SELECT 1 FROM dbo.Campuses WHERE TenantId = @TenantId AND Code = @campusCode AND IsDeleted = 0)
            INSERT dbo.Campuses (TenantId, PublicId, Name, Code, Address, IsHeadOffice, IsActive, CreatedAt, IsDeleted)
            VALUES (@TenantId, NEWID(),
                    CHOOSE(@i, N'Dhaka Main', N'Chattogram', N'Rajshahi', N'Khulna', N'Sylhet', N'Barishal',
                           N'Rangpur', N'Mymensingh', N'Cumilla', N'Gazipur', N'Bogura', N'Jashore') + N' Demo Campus',
                    @campusCode, N'Bangladesh (fictional demonstration address)', IIF(@i = 1, 1, 0), 1, SYSUTCDATETIME(), 0);
        SET @campusId = (SELECT Id FROM dbo.Campuses WHERE TenantId = @TenantId AND Code = @campusCode AND IsDeleted = 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.AcademicYears WHERE TenantId = @TenantId AND Code = @yearCode AND IsDeleted = 0)
            INSERT dbo.AcademicYears (TenantId, CampusId, Name, Code, CycleType, StartDate, EndDate, IsCurrent, IsActive, CreatedAt, IsDeleted)
            VALUES (@TenantId, NULL, N'Academic Year ' + CONVERT(nvarchar(4), 2016 + @i), @yearCode, 1,
                    DATEFROMPARTS(2016 + @i, 1, 1), DATEFROMPARTS(2016 + @i, 12, 31),
                    IIF(@i = 10, 1, 0), 1, SYSUTCDATETIME(), 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.AcademicDepartments WHERE TenantId = @TenantId AND Code = @departmentCode AND IsDeleted = 0)
            INSERT dbo.AcademicDepartments (TenantId, Name, Code, IsActive, CreatedAt, IsDeleted)
            VALUES (@TenantId, CHOOSE(@i, N'Bangla', N'English', N'Mathematics', N'Physics', N'Chemistry',
                    N'Biology', N'Information Technology', N'Business Studies', N'Economics', N'History',
                    N'Geography', N'General Studies'), @departmentCode, 1, SYSUTCDATETIME(), 0);
        SET @departmentId = (SELECT Id FROM dbo.AcademicDepartments WHERE TenantId = @TenantId AND Code = @departmentCode AND IsDeleted = 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.AcademicPrograms WHERE TenantId = @TenantId AND Code = @programCode AND IsDeleted = 0)
            INSERT dbo.AcademicPrograms (TenantId, AcademicDepartmentId, Name, Code, DurationInMonths,
                UsesCreditSystem, IsAdmissionOpen, IsActive, DisplayOrder, CreatedAt, IsDeleted)
            VALUES (@TenantId, @departmentId, N'Demo ' + CHOOSE(@i, N'Bangla', N'English', N'Mathematics',
                    N'Physics', N'Chemistry', N'Biology', N'IT', N'Business', N'Economics', N'History',
                    N'Geography', N'General Studies') + N' Program', @programCode,
                    12, 0, 1, 1, @i, SYSUTCDATETIME(), 0);
        SET @programId = (SELECT Id FROM dbo.AcademicPrograms WHERE TenantId = @TenantId AND Code = @programCode AND IsDeleted = 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.ProgramCampuses WHERE TenantId = @TenantId AND AcademicProgramId = @programId AND CampusId = @campusId AND IsDeleted = 0)
            INSERT dbo.ProgramCampuses (TenantId, AcademicProgramId, CampusId, IsPrimary, IsActive, CreatedAt, IsDeleted)
            VALUES (@TenantId, @programId, @campusId, 1, 1, SYSUTCDATETIME(), 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.AcademicLevels WHERE TenantId = @TenantId AND Code = @levelCode AND IsDeleted = 0)
            INSERT dbo.AcademicLevels (TenantId, AcademicProgramId, Name, Code, LevelNo, IsPromotable,
                IsTerminalLevel, IsActive, DisplayOrder, CreatedAt, IsDeleted)
            VALUES (@TenantId, @programId, N'Level ' + @s, @levelCode, @i,
                    IIF(@i = 12, 0, 1), IIF(@i = 12, 1, 0), 1, @i, SYSUTCDATETIME(), 0);
        SET @levelId = (SELECT Id FROM dbo.AcademicLevels WHERE TenantId = @TenantId AND Code = @levelCode AND IsDeleted = 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.AcademicTracks WHERE TenantId = @TenantId AND Code = @trackCode AND IsDeleted = 0)
            INSERT dbo.AcademicTracks (TenantId, AcademicProgramId, Name, Code, IsDefault, IsActive, DisplayOrder, CreatedAt, IsDeleted)
            VALUES (@TenantId, @programId, N'Demo Track ' + @s, @trackCode, 1, 1, @i, SYSUTCDATETIME(), 0);
        SET @trackId = (SELECT Id FROM dbo.AcademicTracks WHERE TenantId = @TenantId AND Code = @trackCode AND IsDeleted = 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.Mediums WHERE TenantId = @TenantId AND Code = @mediumCode AND IsDeleted = 0)
            INSERT dbo.Mediums (TenantId, Name, Code, IsDefault, IsActive, DisplayOrder, CreatedAt, IsDeleted)
            VALUES (@TenantId, CHOOSE(@i, N'Bangla', N'English', N'Arabic', N'Urdu', N'Hindi', N'French',
                    N'German', N'Spanish', N'Japanese', N'Chinese', N'Korean', N'Turkish'),
                    @mediumCode, IIF(@i = 1, 1, 0), 1, @i, SYSUTCDATETIME(), 0);
        SET @mediumId = (SELECT Id FROM dbo.Mediums WHERE TenantId = @TenantId AND Code = @mediumCode AND IsDeleted = 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.Shifts WHERE TenantId = @TenantId AND Code = @shiftCode AND IsDeleted = 0)
            INSERT dbo.Shifts (TenantId, Name, Code, StartTime, EndTime, IsDefault, IsActive, DisplayOrder, CreatedAt, IsDeleted)
            VALUES (@TenantId, N'Demo Shift ' + @s, @shiftCode, TIMEFROMPARTS(@i + 6, 0, 0, 0, 0),
                    TIMEFROMPARTS(@i + 7, 0, 0, 0, 0), IIF(@i = 1, 1, 0), 1, @i, SYSUTCDATETIME(), 0);
        SET @shiftId = (SELECT Id FROM dbo.Shifts WHERE TenantId = @TenantId AND Code = @shiftCode AND IsDeleted = 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.Rooms WHERE TenantId = @TenantId AND Code = N'DEMO-R' + @s AND IsDeleted = 0)
            INSERT dbo.Rooms (TenantId, CampusId, Name, Code, Capacity, RoomType, IsActive, CreatedAt, IsDeleted)
            VALUES (@TenantId, @campusId, N'Classroom ' + @s, N'DEMO-R' + @s, 45, N'Classroom', 1, SYSUTCDATETIME(), 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.Subjects WHERE TenantId = @TenantId AND Code = @subjectCode AND IsDeleted = 0)
            INSERT dbo.Subjects (TenantId, Name, Code, DefaultCreditHours, IsActive, CreatedAt, IsDeleted)
            VALUES (@TenantId, CHOOSE(@i, N'Bangla', N'English', N'Mathematics', N'Physics', N'Chemistry',
                    N'Biology', N'ICT', N'Accounting', N'Economics', N'History', N'Geography', N'Civics'),
                    @subjectCode, 3, 1, SYSUTCDATETIME(), 0);
        SET @subjectId = (SELECT Id FROM dbo.Subjects WHERE TenantId = @TenantId AND Code = @subjectCode AND IsDeleted = 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.AcademicCurriculums WHERE TenantId = @TenantId AND Code = @curriculumCode AND IsDeleted = 0)
            INSERT dbo.AcademicCurriculums (TenantId, AcademicProgramId, AcademicTrackId, MediumId, Name, Code,
                VersionNo, EffectiveFrom, IsCurrent, IsActive, CreatedAt, IsDeleted)
            VALUES (@TenantId, @programId, @trackId, @mediumId, N'Demo Curriculum ' + @s, @curriculumCode,
                    1, '2026-01-01', 1, 1, SYSUTCDATETIME(), 0);
        SET @curriculumId = (SELECT Id FROM dbo.AcademicCurriculums WHERE TenantId = @TenantId AND Code = @curriculumCode AND IsDeleted = 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.CurriculumSubjects WHERE TenantId = @TenantId AND
                       AcademicCurriculumId = @curriculumId AND AcademicLevelId = @levelId AND SubjectId = @subjectId AND IsDeleted = 0)
            INSERT dbo.CurriculumSubjects (TenantId, AcademicCurriculumId, AcademicLevelId, SubjectId,
                FullMarks, PassMarks, CreditHours, IsOptional, HasPractical, IsActive, DisplayOrder, CreatedAt, IsDeleted)
            VALUES (@TenantId, @curriculumId, @levelId, @subjectId, 100, 33, 3, 0, 0, 1, @i, SYSUTCDATETIME(), 0);
        SET @curriculumSubjectId = (SELECT Id FROM dbo.CurriculumSubjects WHERE TenantId = @TenantId AND
            AcademicCurriculumId = @curriculumId AND AcademicLevelId = @levelId AND SubjectId = @subjectId AND IsDeleted = 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.RoutineTimeSlots WHERE TenantId = @TenantId AND
                       Name = N'Demo Period ' + @s AND IsDeleted = 0)
            INSERT dbo.RoutineTimeSlots (TenantId, Name, StartTime, EndTime, IsBreak, IsActive, DisplayOrder, CreatedAt, IsDeleted)
            VALUES (@TenantId, N'Demo Period ' + @s, TIMEFROMPARTS(@i + 6, 0, 0, 0, 0),
                    TIMEFROMPARTS(@i + 7, 0, 0, 0, 0), 0, 1, @i, SYSUTCDATETIME(), 0);

        SET @i += 1;
    END;

    -- All current batches and sample enrollments share the 2026 academic year.
    SET @yearId = (SELECT Id FROM dbo.AcademicYears WHERE TenantId = @TenantId AND Code = N'DEMO-Y10' AND IsDeleted = 0);

    SET @i = 1;
    WHILE @i <= 12
    BEGIN
        SET @s = RIGHT('00' + CONVERT(varchar(2), @i), 2);
        SET @termCode = N'DEMO-T' + @s;
        IF NOT EXISTS (SELECT 1 FROM dbo.AcademicTerms WHERE TenantId = @TenantId AND
                       AcademicYearId = @yearId AND Code = @termCode AND IsDeleted = 0)
            INSERT dbo.AcademicTerms (TenantId, AcademicYearId, Name, Code, StartDate,
                EndDate, IsCurrent, IsActive, DisplayOrder, CreatedAt, IsDeleted)
            VALUES (@TenantId, @yearId, DATENAME(MONTH, DATEFROMPARTS(2026, @i, 1)) + N' 2026',
                @termCode, DATEFROMPARTS(2026, @i, 1), EOMONTH(DATEFROMPARTS(2026, @i, 1)),
                IIF(@i = 11, 1, 0), 1, @i, SYSUTCDATETIME(), 0);
        SET @i += 1;
    END;
    SET @termId = (SELECT Id FROM dbo.AcademicTerms WHERE TenantId = @TenantId AND
        AcademicYearId = @yearId AND Code = N'DEMO-T11' AND IsDeleted = 0);

    SET @i = 1;
    WHILE @i <= 12
    BEGIN
        SET @s = RIGHT('00' + CONVERT(varchar(2), @i), 2);
        SET @campusCode = N'DEMO-C' + @s;
        SET @programCode = N'DEMO-P' + @s;
        SET @levelCode = N'DEMO-L' + @s;
        SET @trackCode = N'DEMO-TR' + @s;
        SET @mediumCode = N'DEMO-M' + @s;
        SET @shiftCode = N'DEMO-SH' + @s;
        SET @batchCode = N'DEMO-B' + @s;
        SET @curriculumCode = N'DEMO-CUR' + @s;
        SET @studentCode = N'DEMO-ST' + @s;
        SET @subjectCode = N'DEMO-SUB' + @s;

        SET @campusId = (SELECT Id FROM dbo.Campuses WHERE TenantId = @TenantId AND Code = @campusCode AND IsDeleted = 0);
        SET @programId = (SELECT Id FROM dbo.AcademicPrograms WHERE TenantId = @TenantId AND Code = @programCode AND IsDeleted = 0);
        SET @levelId = (SELECT Id FROM dbo.AcademicLevels WHERE TenantId = @TenantId AND Code = @levelCode AND IsDeleted = 0);
        SET @trackId = (SELECT Id FROM dbo.AcademicTracks WHERE TenantId = @TenantId AND Code = @trackCode AND IsDeleted = 0);
        SET @mediumId = (SELECT Id FROM dbo.Mediums WHERE TenantId = @TenantId AND Code = @mediumCode AND IsDeleted = 0);
        SET @shiftId = (SELECT Id FROM dbo.Shifts WHERE TenantId = @TenantId AND Code = @shiftCode AND IsDeleted = 0);
        SET @curriculumId = (SELECT Id FROM dbo.AcademicCurriculums WHERE TenantId = @TenantId AND Code = @curriculumCode AND IsDeleted = 0);
        SET @curriculumSubjectId = (SELECT Id FROM dbo.CurriculumSubjects WHERE TenantId = @TenantId AND
            AcademicCurriculumId = @curriculumId AND AcademicLevelId = @levelId AND IsDeleted = 0);
        SET @subjectId = (SELECT Id FROM dbo.Subjects WHERE TenantId = @TenantId AND Code = @subjectCode AND IsDeleted = 0);
        SET @studentName = CHOOSE(@i, N'Arif Hasan', N'Nusrat Jahan', N'Tanvir Rahman', N'Samia Akter',
            N'Rafiul Islam', N'Mehjabin Chowdhury', N'Sadman Hossain', N'Farzana Islam',
            N'Raihan Ahmed', N'Anika Sultana', N'Mahmudul Hasan', N'Tasnim Rahman');
        SET @guardianName = CHOOSE(@i, N'Mohammad Karim', N'Salma Begum', N'Kamal Uddin', N'Nasima Khatun',
            N'Abdul Hakim', N'Rumana Akter', N'Jahangir Alam', N'Farida Yasmin', N'Jamal Hossain',
            N'Roksana Parvin', N'Khaled Rahman', N'Shahana Begum');

        IF NOT EXISTS (SELECT 1 FROM dbo.AcademicBatches WHERE TenantId = @TenantId AND Code = @batchCode AND IsDeleted = 0)
            INSERT dbo.AcademicBatches (TenantId, CampusId, AcademicYearId, AcademicTermId, AcademicProgramId,
                AcademicLevelId, AcademicTrackId, MediumId, ShiftId, Name, Code, DeliveryMode,
                Capacity, StartDate, EndDate, IsDefault, IsActive, DisplayOrder, CreatedAt, IsDeleted)
            VALUES (@TenantId, @campusId, @yearId, @termId, @programId, @levelId, @trackId, @mediumId, @shiftId,
                N'Demo Batch ' + @s, @batchCode, 1, 45, '2026-01-01', '2026-12-31',
                1, 1, @i, SYSUTCDATETIME(), 0);
        SET @batchId = (SELECT Id FROM dbo.AcademicBatches WHERE TenantId = @TenantId AND Code = @batchCode AND IsDeleted = 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.SubjectOfferings WHERE TenantId = @TenantId AND Code = N'DEMO-OFF' + @s AND IsDeleted = 0)
            INSERT dbo.SubjectOfferings (TenantId, PublicId, AcademicBatchId, CurriculumSubjectId,
                AcademicYearId, AcademicTermId, Code, Capacity, DeliveryMode, IsActive, CreatedAt, IsDeleted)
            VALUES (@TenantId, NEWID(), @batchId, @curriculumSubjectId, @yearId, @termId,
                N'DEMO-OFF' + @s, 45, 1, 1, SYSUTCDATETIME(), 0);
        SET @offeringId = (SELECT Id FROM dbo.SubjectOfferings WHERE TenantId = @TenantId AND Code = N'DEMO-OFF' + @s AND IsDeleted = 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.Students WHERE TenantId = @TenantId AND StudentCode = @studentCode AND IsDeleted = 0)
        BEGIN
            INSERT dbo.Persons (PublicId, FullName, DateOfBirth, Gender, PreferredLanguage, CreatedAt, IsDeleted)
            VALUES (NEWID(), @studentName, DATEFROMPARTS(2010 + @i % 3, 1 + @i % 12, 3 + @i),
                    IIF(@i % 2 = 1, N'Male', N'Female'), N'bn-BD', SYSUTCDATETIME(), 0);
            SET @personId = SCOPE_IDENTITY();
            INSERT dbo.Students (TenantId, PublicId, PersonId, UserId, StudentCode, FullName,
                DateOfBirth, Gender, AdmissionDate, PreferredLanguage, StatusCode, CreatedAt, IsDeleted)
            VALUES (@TenantId, NEWID(), @personId, IIF(@i = 1, @StudentUserId, NULL),
                @studentCode, @studentName, DATEFROMPARTS(2010 + @i % 3, 1 + @i % 12, 3 + @i),
                IIF(@i % 2 = 1, N'Male', N'Female'), '2026-01-10', N'bn-BD', N'Active', SYSUTCDATETIME(), 0);
        END;
        SET @studentId = (SELECT Id FROM dbo.Students WHERE TenantId = @TenantId AND StudentCode = @studentCode AND IsDeleted = 0);
        SET @personId = (SELECT PersonId FROM dbo.Students WHERE Id = @studentId);

        IF NOT EXISTS (SELECT 1 FROM dbo.StudentPersonLinks WHERE TenantId = @TenantId AND StudentId = @studentId AND PersonId = @personId AND IsDeleted = 0)
            INSERT dbo.StudentPersonLinks (TenantId, StudentId, PersonId, IsPrimary, LinkedAt, CreatedAt, IsDeleted)
            VALUES (@TenantId, @studentId, @personId, 1, SYSUTCDATETIME(), SYSUTCDATETIME(), 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.Guardians WHERE TenantId = @TenantId AND
                       Email = N'guardian' + @s + N'@eduos.example.invalid' AND IsDeleted = 0)
        BEGIN
            INSERT dbo.Persons (PublicId, FullName, Gender, PreferredLanguage, CreatedAt, IsDeleted)
            VALUES (NEWID(), @guardianName, IIF(@i % 2 = 1, N'Male', N'Female'), N'bn-BD', SYSUTCDATETIME(), 0);
            SET @guardianPersonId = SCOPE_IDENTITY();
            INSERT dbo.Guardians (TenantId, PublicId, PersonId, UserId, FullName, Email, IsActive, CreatedAt, IsDeleted)
            VALUES (@TenantId, NEWID(), @guardianPersonId, IIF(@i = 1, @ParentUserId, NULL),
                @guardianName, N'guardian' + @s + N'@eduos.example.invalid', 1, SYSUTCDATETIME(), 0);
        END;
        SET @guardianId = (SELECT Id FROM dbo.Guardians WHERE TenantId = @TenantId AND
            Email = N'guardian' + @s + N'@eduos.example.invalid' AND IsDeleted = 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.StudentGuardians WHERE TenantId = @TenantId AND StudentId = @studentId AND GuardianId = @guardianId AND IsDeleted = 0)
            INSERT dbo.StudentGuardians (TenantId, StudentId, GuardianId, RelationCode, IsPrimary,
                CanReceiveSms, CanReceiveEmail, IsAuthorizedPickup, IsFinancialContact,
                IsEmergencyContact, CreatedAt, IsDeleted)
            VALUES (@TenantId, @studentId, @guardianId, IIF(@i % 2 = 1, N'Father', N'Mother'),
                1, 0, 0, 1, 1, 1, SYSUTCDATETIME(), 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.StudentEnrollments WHERE TenantId = @TenantId AND
                       StudentId = @studentId AND AcademicBatchId = @batchId AND IsDeleted = 0)
            INSERT dbo.StudentEnrollments (TenantId, PublicId, ClientRequestId, StudentId,
                CampusId, AcademicYearId, AcademicTermId, AcademicProgramId, AcademicLevelId, AcademicBatchId,
                AcademicCurriculumId, AcademicTrackId, MediumId, ShiftId, RollNo,
                RegistrationNo, EnrollmentDate, State, IsCurrent, CreatedAt, IsDeleted)
            VALUES (@TenantId, NEWID(), NEWID(), @studentId, @campusId, @yearId, @termId,
                @programId, @levelId, @batchId, @curriculumId, @trackId, @mediumId, @shiftId,
                N'DEMO-ROLL' + @s, N'DEMO-REG' + @s, '2026-01-10', 1, 1, SYSUTCDATETIME(), 0);
        SET @enrollmentId = (SELECT Id FROM dbo.StudentEnrollments WHERE TenantId = @TenantId AND
            StudentId = @studentId AND AcademicBatchId = @batchId AND IsDeleted = 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.StudentSubjectRegistrations WHERE TenantId = @TenantId AND
                       StudentEnrollmentId = @enrollmentId AND SubjectOfferingId = @offeringId AND IsDeleted = 0)
            INSERT dbo.StudentSubjectRegistrations (TenantId, ClientRequestId, StudentEnrollmentId,
                SubjectOfferingId, State, RegisteredAt, CreditHoursSnapshot, CreatedAt, IsDeleted)
            VALUES (@TenantId, NEWID(), @enrollmentId, @offeringId, 2,
                SYSUTCDATETIME(), 3, SYSUTCDATETIME(), 0);

        SET @i += 1;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

-- Audit the demo rows; the script never claims to populate immutable ledgers,
-- security/audit/system tables or every EF entity just to hit a count.
SELECT TableName, DemoRows
FROM (VALUES
    (N'Campuses', (SELECT COUNT(*) FROM dbo.Campuses WHERE TenantId = @TenantId AND Code LIKE N'DEMO-C%')),
    (N'AcademicYears', (SELECT COUNT(*) FROM dbo.AcademicYears WHERE TenantId = @TenantId AND Code LIKE N'DEMO-Y%')),
    (N'AcademicTerms', (SELECT COUNT(*) FROM dbo.AcademicTerms WHERE TenantId = @TenantId AND Code LIKE N'DEMO-T%')),
    (N'AcademicDepartments', (SELECT COUNT(*) FROM dbo.AcademicDepartments WHERE TenantId = @TenantId AND Code LIKE N'DEMO-D%')),
    (N'AcademicPrograms', (SELECT COUNT(*) FROM dbo.AcademicPrograms WHERE TenantId = @TenantId AND Code LIKE N'DEMO-P%')),
    (N'AcademicLevels', (SELECT COUNT(*) FROM dbo.AcademicLevels WHERE TenantId = @TenantId AND Code LIKE N'DEMO-L%')),
    (N'AcademicBatches', (SELECT COUNT(*) FROM dbo.AcademicBatches WHERE TenantId = @TenantId AND Code LIKE N'DEMO-B%')),
    (N'Subjects', (SELECT COUNT(*) FROM dbo.Subjects WHERE TenantId = @TenantId AND Code LIKE N'DEMO-SUB%')),
    (N'Students', (SELECT COUNT(*) FROM dbo.Students WHERE TenantId = @TenantId AND StudentCode LIKE N'DEMO-ST%')),
    (N'StudentEnrollments', (SELECT COUNT(*) FROM dbo.StudentEnrollments WHERE TenantId = @TenantId AND RollNo LIKE N'DEMO-ROLL%')),
    (N'Guardians', (SELECT COUNT(*) FROM dbo.Guardians WHERE TenantId = @TenantId AND Email LIKE N'guardian%@eduos.example.invalid')),
    (N'StudentGuardians', (SELECT COUNT(*) FROM dbo.StudentGuardians WHERE TenantId = @TenantId AND StudentId IN
        (SELECT Id FROM dbo.Students WHERE TenantId = @TenantId AND StudentCode LIKE N'DEMO-ST%'))),
    (N'SubjectOfferings', (SELECT COUNT(*) FROM dbo.SubjectOfferings WHERE TenantId = @TenantId AND Code LIKE N'DEMO-OFF%'))
) AS Coverage(TableName, DemoRows)
ORDER BY TableName;
