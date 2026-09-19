using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913001000_NormalizeAttendanceLeaveRelations")]
public partial class NormalizeAttendanceLeaveRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[LeaveApplications]', N'U') IS NOT NULL
BEGIN
    DECLARE @leaveFk nvarchar(max)=N'';
    SELECT @leaveFk += N'ALTER TABLE [dbo].[LeaveApplications] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[LeaveApplications]') AND c.name IN (N'LeaveTypeId',N'LeaveTypeId1');
    IF LEN(@leaveFk)>0 EXEC sp_executesql @leaveFk;

    DECLARE @leaveIx nvarchar(max)=N'';
    SELECT @leaveIx += N'DROP INDEX ['+i.name+N'] ON [dbo].[LeaveApplications];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[LeaveApplications]') AND c.name IN (N'LeaveTypeId',N'LeaveTypeId1') AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@leaveIx)>0 EXEC sp_executesql @leaveIx;

    IF COL_LENGTH('dbo.LeaveApplications','LeaveTypeId1') IS NOT NULL
    BEGIN
        IF EXISTS(
            SELECT 1 FROM [dbo].[LeaveApplications]
            WHERE [LeaveTypeId1] IS NOT NULL AND [LeaveTypeId] IS NOT NULL AND CONVERT(bigint,[LeaveTypeId])<>CONVERT(bigint,[LeaveTypeId1])
        ) THROW 51000, 'Cannot normalize LeaveApplications.LeaveTypeId because canonical and shadow references conflict.', 1;

        ALTER TABLE [dbo].[LeaveApplications] ADD [LeaveTypeIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[LeaveApplications] SET [LeaveTypeIdNormalized]=COALESCE(CONVERT(bigint,[LeaveTypeId1]),CONVERT(bigint,[LeaveTypeId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[LeaveApplications] WHERE [LeaveTypeIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize LeaveApplications.LeaveTypeId because required references are null.', 1;
        ALTER TABLE [dbo].[LeaveApplications] DROP COLUMN [LeaveTypeId];
        ALTER TABLE [dbo].[LeaveApplications] DROP COLUMN [LeaveTypeId1];
        EXEC sp_rename N'dbo.LeaveApplications.LeaveTypeIdNormalized', N'LeaveTypeId', 'COLUMN';
    END
    ELSE
        ALTER TABLE [dbo].[LeaveApplications] ALTER COLUMN [LeaveTypeId] BIGINT NOT NULL;

    ALTER TABLE [dbo].[LeaveApplications] ALTER COLUMN [LeaveTypeId] BIGINT NOT NULL;
    ALTER TABLE [dbo].[LeaveApplications] ALTER COLUMN [UserId] BIGINT NOT NULL;
    ALTER TABLE [dbo].[LeaveApplications] ALTER COLUMN [ApprovedBy] BIGINT NULL;

    IF OBJECT_ID(N'[dbo].[LeaveTypes]', N'U') IS NULL
        THROW 51000, 'Cannot normalize LeaveApplications because LeaveTypes does not exist.', 1;
    IF EXISTS(
        SELECT 1 FROM [dbo].[LeaveApplications] r
        WHERE NOT EXISTS(SELECT 1 FROM [dbo].[LeaveTypes] p WHERE p.[Id]=r.[LeaveTypeId])
    ) THROW 51000, 'Cannot normalize LeaveApplications.LeaveTypeId because orphaned references exist.', 1;
    IF EXISTS(
        SELECT 1 FROM [dbo].[LeaveApplications] r
        JOIN [dbo].[LeaveTypes] p ON p.[Id]=r.[LeaveTypeId]
        WHERE r.[TenantId]<>p.[TenantId]
    ) THROW 51000, 'Cannot normalize LeaveApplications.LeaveTypeId because cross-tenant references exist.', 1;

    IF EXISTS(SELECT 1 FROM [dbo].[LeaveApplications] WHERE UPPER(LTRIM(RTRIM([UserType]))) NOT IN (N'STUDENT',N'EMPLOYEE'))
        THROW 51000, 'Cannot normalize LeaveApplications.UserId because unsupported UserType values exist.', 1;
    IF OBJECT_ID(N'[dbo].[Students]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[Employees]', N'U') IS NULL
        THROW 51000, 'Cannot normalize LeaveApplications.UserId because Students or Employees does not exist.', 1;
    IF EXISTS(
        SELECT 1 FROM [dbo].[LeaveApplications] r
        WHERE UPPER(LTRIM(RTRIM(r.[UserType])))=N'STUDENT'
          AND NOT EXISTS(SELECT 1 FROM [dbo].[Students] p WHERE p.[Id]=r.[UserId] AND p.[TenantId]=r.[TenantId])
    ) THROW 51000, 'Cannot normalize LeaveApplications.UserId because orphaned or cross-tenant student references exist.', 1;
    IF EXISTS(
        SELECT 1 FROM [dbo].[LeaveApplications] r
        WHERE UPPER(LTRIM(RTRIM(r.[UserType])))=N'EMPLOYEE'
          AND NOT EXISTS(SELECT 1 FROM [dbo].[Employees] p WHERE p.[Id]=r.[UserId] AND p.[TenantId]=r.[TenantId])
    ) THROW 51000, 'Cannot normalize LeaveApplications.UserId because orphaned or cross-tenant employee references exist.', 1;

    IF COL_LENGTH('dbo.AspNetUsers','Id') IS NOT NULL AND EXISTS(
        SELECT 1 FROM [dbo].[LeaveApplications] r
        WHERE r.[ApprovedBy] IS NOT NULL AND NOT EXISTS(SELECT 1 FROM [dbo].[AspNetUsers] u WHERE u.[Id]=r.[ApprovedBy])
    ) THROW 51000, 'Cannot normalize LeaveApplications.ApprovedBy because orphaned user references exist.', 1;
    IF COL_LENGTH('dbo.AspNetUsers','TenantId') IS NOT NULL AND EXISTS(
        SELECT 1 FROM [dbo].[LeaveApplications] r
        JOIN [dbo].[AspNetUsers] u ON u.[Id]=r.[ApprovedBy]
        WHERE r.[ApprovedBy] IS NOT NULL AND u.[TenantId] IS NOT NULL AND u.[TenantId]<>r.[TenantId]
    ) THROW 51000, 'Cannot normalize LeaveApplications.ApprovedBy because cross-tenant approver references exist.', 1;

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[LeaveApplications]') AND name=N'IX_LeaveApplications_LeaveTypeId')
        CREATE INDEX [IX_LeaveApplications_LeaveTypeId] ON [dbo].[LeaveApplications]([LeaveTypeId]);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[LeaveApplications]') AND name=N'FK_LeaveApplications_LeaveTypes_LeaveTypeId')
        ALTER TABLE [dbo].[LeaveApplications] WITH CHECK ADD CONSTRAINT [FK_LeaveApplications_LeaveTypes_LeaveTypeId] FOREIGN KEY([LeaveTypeId]) REFERENCES [dbo].[LeaveTypes]([Id]) ON DELETE NO ACTION;
END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }
}
