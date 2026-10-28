using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;

namespace EduOS.Service.Services.Students;

/// <summary>
/// Atomic, retry-safe execution for a maximum of 100 rows per upload. Larger migrations
/// resume by submitting successive source-key-disjoint batches; no global Person matching
/// is performed without a separately verified, consent-governed identity workflow.
/// </summary>
public sealed partial class StudentImportExecutionService : IStudentImportExecutionService
{
    private const int MaxBatchRows = 100;
    private const string BatchScope = "StudentImportBatch";
    private const string SourceScope = "StudentImportSource";
    private const string GuardianSourceScope = "StudentImportGuardianSource";
    private readonly IStudentImportPreviewService _preview;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<StudentPersonLink> _links;
    private readonly IGenericRepository<Person> _persons;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<StudentGuardian> _studentGuardians;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicCurriculum> _curricula;
    private readonly IGenericRepository<ImportLog> _logs;
    private readonly IGenericRepository<ImportLogItem> _items;
    private readonly IGenericRepository<IdempotencyRecord> _keys;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly ILogger<StudentImportExecutionService> _logger;

    public StudentImportExecutionService(IStudentImportPreviewService preview,
        IGenericRepository<Student> students, IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<StudentPersonLink> links, IGenericRepository<Person> persons,
        IGenericRepository<Guardian> guardians, IGenericRepository<StudentGuardian> studentGuardians,
        IGenericRepository<AcademicBatch> batches, IGenericRepository<AcademicCurriculum> curricula,
        IGenericRepository<ImportLog> logs, IGenericRepository<ImportLogItem> items,
        IGenericRepository<IdempotencyRecord> keys, IUnitOfWork uow,
        ICurrentUserService user, ILogger<StudentImportExecutionService> logger)
    {
        _preview = preview; _students = students; _enrollments = enrollments;
        _links = links; _persons = persons; _guardians = guardians;
        _studentGuardians = studentGuardians; _batches = batches; _curricula = curricula;
        _logs = logs; _items = items; _keys = keys; _uow = uow;
        _user = user; _logger = logger;
    }

    public Task<ApiResponse<StudentImportExecutionDto>> ExecuteAsync(Stream content, string extension,
        IReadOnlyDictionary<string, string> mapping, Guid clientRequestId, CancellationToken cancellationToken = default)
        => ExecuteInternalAsync(content, extension, mapping, clientRequestId, 0, null, cancellationToken);

    /// <summary>
    /// Execute one 100-row window from a validated, at-most-5,000-row CSV/XLSX.
    /// The entire original file hash is part of the idempotency contract: changing
    /// any row after a partial import cannot silently resume an old request.
    /// No file or raw learner information is persisted between HTTP requests.
    /// </summary>
    public async Task<ApiResponse<StudentImportExecutionDto>> ExecuteSliceAsync(Stream content, string extension,
        IReadOnlyDictionary<string, string> mapping, Guid clientRequestId, int rowOffset,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<StudentImportExecutionDto>.ErrorResponse("Student import access denied.", 403);
        if (clientRequestId == Guid.Empty || mapping == null || content == null || !content.CanRead || !content.CanSeek ||
            rowOffset < 0 || rowOffset >= 5000 || rowOffset % MaxBatchRows != 0)
            return ApiResponse<StudentImportExecutionDto>.ErrorResponse("Invalid file, mapping, request ID or 100-row offset.", 400);
        try
        {
            using var buffer = new MemoryStream();
            content.Position = 0;
            await content.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length is <= 0 or > 6291456)
                return ApiResponse<StudentImportExecutionDto>.ErrorResponse("File must be at most 6 MB.", 413);
            var bytes = buffer.ToArray();
            var allRows = ReadRows(bytes, extension, mapping, 5000);
            if (rowOffset >= allRows.Count)
                return ApiResponse<StudentImportExecutionDto>.ErrorResponse("Offset is beyond the end of the source file.", 400);
            var slice = allRows.Skip(rowOffset).Take(MaxBatchRows).ToArray();
            using var csv = new MemoryStream(BuildCsvSlice(slice), writable: false);
            var originHash = Convert.ToHexString(SHA256.HashData(bytes));
            return await ExecuteInternalAsync(csv, ".csv", CanonicalMapping, clientRequestId,
                rowOffset, originHash, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException)
        {
            return ApiResponse<StudentImportExecutionDto>.ErrorResponse("Invalid source file or mapping: " + ex.Message, 400);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student import source slicing failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<StudentImportExecutionDto>.ErrorResponse("Unable to read this source-file batch.", 500);
        }
    }

    private static readonly string[] CanonicalFields =
        ["SourceKey", "StudentCode", "FullName", "CampusId", "AcademicBatchId", "RollNo",
         "DateOfBirth", "GuardianName", "GuardianPhone", "GuardianSourceKey"];
    private static readonly IReadOnlyDictionary<string, string> CanonicalMapping =
        CanonicalFields.ToDictionary(x => x, x => x, StringComparer.OrdinalIgnoreCase);

    private static byte[] BuildCsvSlice(IEnumerable<ImportRow> rows)
    {
        var builder = new StringBuilder(string.Join(",", CanonicalFields)).Append('\n');
        foreach (var row in rows)
        {
            var cells = new[]
            {
                row.SourceKey, row.StudentCode, row.FullName, row.CampusId,
                row.BatchId.ToString(CultureInfo.InvariantCulture), row.RollNo,
                row.DateOfBirth, row.GuardianName, row.GuardianPhone, row.GuardianSourceKey
            };
            builder.AppendJoin(",", cells.Select(value => "\"" + value.Replace("\"", "\"\"") + "\""))
                .Append('\n');
        }
        return new UTF8Encoding(false).GetBytes(builder.ToString());
    }

    private async Task<ApiResponse<StudentImportExecutionDto>> ExecuteInternalAsync(Stream content, string extension,
        IReadOnlyDictionary<string, string> mapping, Guid clientRequestId, int sourceRowOffset,
        string? originalFileHash, CancellationToken cancellationToken)
    {
        if (!CanManage()) return ApiResponse<StudentImportExecutionDto>.ErrorResponse("Student import access denied.", 403);
        if (clientRequestId == Guid.Empty || mapping == null || !content.CanSeek || !content.CanRead)
            return ApiResponse<StudentImportExecutionDto>.ErrorResponse("Valid file, mapping and request ID are required.");
        try
        {
            using var buffer = new MemoryStream();
            content.Position = 0;
            await content.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length is <= 0 or > 6291456)
                return ApiResponse<StudentImportExecutionDto>.ErrorResponse("File must be at most 6 MB.", 413);
            var bytes = buffer.ToArray();
            var canonicalMapping = JsonSerializer.Serialize(mapping.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new { Field = x.Key.ToUpperInvariant(), Column = x.Value?.Trim() }));
            var hashInput = Convert.ToHexString(SHA256.HashData(bytes)) + "|" + extension.ToLowerInvariant() + "|" + canonicalMapping;
            if (originalFileHash != null)
                hashInput += "|" + originalFileHash + "|" + sourceRowOffset.ToString(CultureInfo.InvariantCulture);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));
            var rows = ReadRows(bytes, extension, mapping);
            if (rows.Count is < 1 or > MaxBatchRows)
                return ApiResponse<StudentImportExecutionDto>.ErrorResponse("Execution requires 1–100 rows per batch. Split the source file into bounded batches.", 400);
            var tenant = _user.TenantId;
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var previous = await _keys.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Scope == BatchScope && x.RequestId == clientRequestId && !x.IsDeleted, token);
                if (previous != null)
                {
                    if (previous.RequestHash != hash || !previous.TargetId.HasValue)
                        return ApiResponse<StudentImportExecutionDto>.ErrorResponse("Import request ID was already used with different content.", 409);
                    var priorLog = await _logs.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.Id == previous.TargetId && x.ImportType == "Student" && !x.IsDeleted, token);
                    if (priorLog == null || !priorLog.CompletedAt.HasValue)
                        return ApiResponse<StudentImportExecutionDto>.ErrorResponse("Previous import batch is not reconciled.", 409);
                    return ApiResponse<StudentImportExecutionDto>.SuccessResponse(new StudentImportExecutionDto
                    {
                        ImportReference = priorLog.PublicId, TotalRows = priorLog.TotalRows,
                        ImportedRows = priorLog.SuccessRows, Replayed = true
                    }, "Previously committed import batch returned.");
                }

                // Re-run canonical validation inside the serializable write transaction.
                using (var previewStream = new MemoryStream(bytes, writable: false))
                {
                    var check = await _preview.PreviewAsync(previewStream, extension, mapping, token);
                    if (!check.Success)
                        return ApiResponse<StudentImportExecutionDto>.ErrorResponse(check.Message, check.StatusCode);
                    if (check.Data == null || check.Data.InvalidRows > 0 || check.Data.ValidRows != rows.Count)
                        return ApiResponse<StudentImportExecutionDto>.ErrorResponse(
                            "Import validation changed or contains invalid rows. Run preview and correct the source.", 409);
                }

                var batchIds = rows.Select(x => x.BatchId).Distinct().OrderBy(x => x).ToArray();
                var batches = await _batches.GetQueryable().Where(x => x.TenantId == tenant &&
                    !x.IsDeleted && x.IsActive && batchIds.Contains(x.Id))
                    .OrderBy(x => x.Id).ToListAsync(token);
                if (batches.Count != batchIds.Length)
                    return ApiResponse<StudentImportExecutionDto>.ErrorResponse("An academic batch is no longer available.", 409);
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var curricula = new Dictionary<long, long>();
                foreach (var batch in batches)
                {
                    var matches = await _curricula.GetQueryable().AsNoTracking().Where(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.IsActive && x.IsCurrent &&
                        x.AcademicProgramId == batch.AcademicProgramId &&
                        x.AcademicTrackId == batch.AcademicTrackId && x.MediumId == batch.MediumId &&
                        x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today))
                        .Select(x => x.Id).Take(2).ToListAsync(token);
                    if (matches.Count != 1)
                        return ApiResponse<StudentImportExecutionDto>.ErrorResponse("Exactly one current curriculum must match every imported batch.", 409);
                    curricula.Add(batch.Id, matches[0]);
                }

                // Acquire write locks in deterministic batch order; admission and academic
                // enrollment allocation use the same batch rowversion lock.
                var now = DateTime.UtcNow;
                foreach (var batch in batches)
                {
                    batch.UpdatedAt = now;
                    batch.UpdatedBy = _user.UserId;
                    _batches.Update(batch);
                }
                await _uow.SaveChangesAsync(token);
                using (var lockedPreview = new MemoryStream(bytes, writable: false))
                {
                    var check = await _preview.PreviewAsync(lockedPreview, extension, mapping, token);
                    if (!check.Success || check.Data == null || check.Data.InvalidRows > 0 || check.Data.ValidRows != rows.Count)
                        throw new ImportConflictException("Import data changed during allocation. No records were committed.");
                }

                // Existing unique tenant+scope+request indexes enforce source-key
                // reconciliation even when separate batches target different campuses.
                // Check the entire bounded 100-row source window in one tenant-scoped
                // SQL query instead of one database round-trip per learner. The
                // tenant/scope/request unique index still arbitrates competing writers.
                var sourceIds = rows.Select(row => SourceId(row.SourceKey)).Distinct().ToArray();
                if (await _keys.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Scope == SourceScope && !x.IsDeleted &&
                    sourceIds.Contains(x.RequestId), token))
                    throw new ImportConflictException("A source key has already been imported.");

                // Guardian source keys are explicit, tenant-owned references. A name or phone
                // match alone must never link or merge people, including across institutes.
                var guardianRows = rows.Where(x => !string.IsNullOrWhiteSpace(x.GuardianSourceKey)).ToArray();
                var guardianSourceIds = guardianRows.Select(x => GuardianSourceId(x.GuardianSourceKey))
                    .Distinct().ToArray();
                var priorGuardianKeys = await _keys.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.Scope == GuardianSourceScope &&
                        !x.IsDeleted && guardianSourceIds.Contains(x.RequestId))
                    .Select(x => new { x.RequestId, x.RequestHash, x.TargetId }).ToListAsync(token);
                var priorGuardianIds = priorGuardianKeys.Where(x => x.TargetId.HasValue)
                    .Select(x => x.TargetId!.Value).Distinct().ToArray();
                var priorGuardians = await _guardians.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                        priorGuardianIds.Contains(x.Id))
                    .Select(x => new { x.Id, x.FullName, x.Phone }).ToDictionaryAsync(x => x.Id, token);
                var guardianBySource = priorGuardianKeys.ToDictionary(x => x.RequestId);
                var guardianCache = new Dictionary<Guid, long>();
                foreach (var row in guardianRows)
                {
                    var key = GuardianSourceId(row.GuardianSourceKey);
                    if (!guardianBySource.TryGetValue(key, out var prior)) continue;
                    if (prior.RequestHash != GuardianProfileHash(row) || !prior.TargetId.HasValue ||
                        !priorGuardians.TryGetValue(prior.TargetId.Value, out var guardian) ||
                        !string.Equals(guardian.FullName.Trim(), row.GuardianName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals((guardian.Phone ?? "").Trim(), row.GuardianPhone.Trim(), StringComparison.Ordinal))
                        throw new ImportConflictException("Guardian source key conflicts with an existing guardian profile. Resolve the source identity before importing.");
                    guardianCache[key] = guardian.Id;
                }

                var log = new ImportLog
                {
                    TenantId = tenant, ImportType = "Student", TotalRows = rows.Count,
                    StartedAt = now, CompletedAt = now, SuccessRows = rows.Count,
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _logs.AddAsync(log);
                await _uow.SaveChangesAsync(token);

                foreach (var row in rows)
                {
                    var birth = string.IsNullOrWhiteSpace(row.DateOfBirth)
                        ? (DateOnly?)null
                        : DateOnly.ParseExact(row.DateOfBirth, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var person = new Person
                    {
                        FullName = row.FullName, DateOfBirth = birth,
                        CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _persons.AddAsync(person);
                    await _uow.SaveChangesAsync(token);
                    var student = new Student
                    {
                        TenantId = tenant, PersonId = person.Id, StudentCode = row.StudentCode,
                        FullName = row.FullName, DateOfBirth = birth, AdmissionDate = today,
                        CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _students.AddAsync(student);
                    await _uow.SaveChangesAsync(token);
                    await _links.AddAsync(new StudentPersonLink
                    {
                        TenantId = tenant, StudentId = student.Id, PersonId = person.Id,
                        IsPrimary = true, LinkedAt = now, LinkedByUserId = _user.UserId,
                        CreatedAt = now, CreatedBy = _user.UserId
                    });
                    var batch = batches.Single(x => x.Id == row.BatchId);
                    await _enrollments.AddAsync(new StudentEnrollment
                    {
                        TenantId = tenant, ClientRequestId = Guid.NewGuid(),
                        StudentId = student.Id, CampusId = batch.CampusId,
                        AcademicYearId = batch.AcademicYearId, AcademicTermId = batch.AcademicTermId,
                        AcademicProgramId = batch.AcademicProgramId, AcademicLevelId = batch.AcademicLevelId,
                        AcademicBatchId = batch.Id, AcademicCurriculumId = curricula[batch.Id],
                        AcademicTrackId = batch.AcademicTrackId, MediumId = batch.MediumId, ShiftId = batch.ShiftId,
                        RollNo = row.RollNo, EnrollmentDate = today,
                        State = EnrollmentState.Active, IsCurrent = true,
                        CreatedAt = now, CreatedBy = _user.UserId
                    });
                    if (!string.IsNullOrWhiteSpace(row.GuardianName))
                    {
                        var guardianKey = string.IsNullOrWhiteSpace(row.GuardianSourceKey)
                            ? (Guid?)null : GuardianSourceId(row.GuardianSourceKey);
                        long guardianId;
                        if (guardianKey.HasValue && guardianCache.TryGetValue(guardianKey.Value, out var reusedId))
                            guardianId = reusedId;
                        else
                        {
                            // Never infer identity from name/phone. Only an explicitly mapped,
                            // immutable tenant source key permits guardian reuse.
                            var guardianPerson = new Person
                            {
                                FullName = row.GuardianName, Phone = EmptyToNull(row.GuardianPhone),
                                CreatedAt = now, CreatedBy = _user.UserId
                            };
                            await _persons.AddAsync(guardianPerson);
                            await _uow.SaveChangesAsync(token);
                            var guardian = new Guardian
                            {
                                TenantId = tenant, PersonId = guardianPerson.Id,
                                FullName = row.GuardianName, Phone = EmptyToNull(row.GuardianPhone),
                                CreatedAt = now, CreatedBy = _user.UserId
                            };
                            await _guardians.AddAsync(guardian);
                            await _uow.SaveChangesAsync(token);
                            guardianId = guardian.Id;
                            if (guardianKey.HasValue)
                            {
                                await _keys.AddAsync(new IdempotencyRecord
                                {
                                    TenantId = tenant, Scope = GuardianSourceScope,
                                    RequestId = guardianKey.Value, RequestHash = GuardianProfileHash(row),
                                    TargetId = guardianId, CompletedByUserId = _user.UserId,
                                    CompletedAt = now, ExpiresAt = now.AddYears(50),
                                    CreatedAt = now, CreatedBy = _user.UserId
                                });
                                guardianCache[guardianKey.Value] = guardianId;
                            }
                        }
                        await _studentGuardians.AddAsync(new StudentGuardian
                        {
                            TenantId = tenant, StudentId = student.Id, GuardianId = guardianId,
                            RelationCode = "Guardian", IsPrimary = true,
                            CreatedAt = now, CreatedBy = _user.UserId
                        });
                    }
                    await _items.AddAsync(new ImportLogItem
                    {
                        TenantId = tenant, ImportLogId = log.Id, RowNumber = row.RowNumber + sourceRowOffset,
                        BusinessKey = row.SourceKey, IsSuccess = true,
                        CreatedAt = now, CreatedBy = _user.UserId
                    });
                    await _keys.AddAsync(new IdempotencyRecord
                    {
                        TenantId = tenant, Scope = SourceScope, RequestId = SourceId(row.SourceKey),
                        RequestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.SourceKey.ToUpperInvariant()))),
                        TargetId = student.Id, CompletedByUserId = _user.UserId, CompletedAt = now,
                        ExpiresAt = now.AddYears(50), CreatedAt = now, CreatedBy = _user.UserId
                    });
                }
                await _uow.SaveChangesAsync(token);
                await _keys.AddAsync(new IdempotencyRecord
                {
                    TenantId = tenant, Scope = BatchScope, RequestId = clientRequestId,
                    RequestHash = hash, TargetId = log.Id,
                    CompletedByUserId = _user.UserId, CompletedAt = now,
                    ExpiresAt = now.AddYears(50), CreatedAt = now, CreatedBy = _user.UserId
                });
                await _uow.SaveChangesAsync(token);
                return ApiResponse<StudentImportExecutionDto>.SuccessResponse(new StudentImportExecutionDto
                {
                    ImportReference = log.PublicId, TotalRows = rows.Count, ImportedRows = rows.Count
                }, "Student import batch committed and reconciled.");
            }, IsolationLevel.Serializable, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ImportConflictException ex)
        {
            return ApiResponse<StudentImportExecutionDto>.ErrorResponse(ex.Message, 409);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException)
        {
            return ApiResponse<StudentImportExecutionDto>.ErrorResponse("Invalid import file or mapping: " + ex.Message, 400);
        }
        catch (Exception ex) when (IsRetryableSqlAllocationConflict(ex))
        {
            // EF can wrap SQL Server's deadlock victim in DbUpdateException and
            // InvalidOperationException. The unit of work rolls back before this
            // handler; only recognized SQL lock conflicts are retryable.
            _logger.LogWarning("Concurrent student import allocation conflict for tenant {TenantId}", _user.TenantId);
            return ApiResponse<StudentImportExecutionDto>.ErrorResponse(
                "Another import changed this allocation. Re-run preview and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Student import integrity conflict for tenant {TenantId}", _user.TenantId);
            return ApiResponse<StudentImportExecutionDto>.ErrorResponse("Import conflicts with existing records. Re-run preview.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student import execution failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<StudentImportExecutionDto>.ErrorResponse("Import batch could not be committed.", 500);
        }
    }

    // Do not convert arbitrary EF/SQL failures to a conflict. Only a known
    // provider exception, optionally inside EF's observed wrappers, qualifies.
    private static bool IsRetryableSqlAllocationConflict(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is Microsoft.Data.SqlClient.SqlException sql)
                return sql.Number is 1205 or 3960 or 1222;
            if (current is not InvalidOperationException && current is not DbUpdateException)
                return false;
        }
        return false;
    }

    public async Task<ApiResponse<StudentImportReconciliationDto>> GetAsync(Guid importReference,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<StudentImportReconciliationDto>.ErrorResponse("Student import access denied.", 403);
        if (importReference == Guid.Empty || page < 1 || pageSize is < 1 or > 100)
            return ApiResponse<StudentImportReconciliationDto>.ErrorResponse("Invalid import reference or pagination.");
        var tenant = _user.TenantId;
        var log = await _logs.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == importReference && x.ImportType == "Student" && !x.IsDeleted, cancellationToken);
        if (log == null) return ApiResponse<StudentImportReconciliationDto>.ErrorResponse("Import batch not found.", 404);
        var query = _items.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.ImportLogId == log.Id && !x.IsDeleted);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.RowNumber)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize)
            .Select(x => new StudentImportReconciliationItemDto
            {
                RowNumber = x.RowNumber, SourceKey = x.BusinessKey ?? "",
                IsSuccess = x.IsSuccess, ErrorMessage = x.ErrorMessage
            }).ToListAsync(cancellationToken);
        return ApiResponse<StudentImportReconciliationDto>.SuccessResponse(new StudentImportReconciliationDto
        {
            ImportReference = log.PublicId, TotalRows = log.TotalRows,
            SuccessRows = log.SuccessRows, FailedRows = log.FailedRows,
            StartedAt = log.StartedAt, CompletedAt = log.CompletedAt,
            Page = page, PageSize = pageSize, TotalItems = total, Items = items
        });
    }

    private bool CanManage() => _user.IsAuthenticated && _user.TenantId > 0 && _user.IsTenantAdmin;
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static Guid GuardianSourceId(string key) => SourceId("GUARDIAN:" + key);
    private static string GuardianProfileHash(ImportRow row) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(row.GuardianName.Trim().ToUpperInvariant() + "\u001F" + row.GuardianPhone.Trim())));

    private static Guid SourceId(string sourceKey)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey.Trim().ToUpperInvariant()));
        return new Guid(digest.AsSpan(0, 16));
    }

    private static List<ImportRow> ReadRows(byte[] bytes, string extension, IReadOnlyDictionary<string, string> mapping, int maxRows = MaxBatchRows)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        string[] headers;
        var cells = new List<string[]>();
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            using var parser = new TextFieldParser(reader) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
            parser.SetDelimiters(",");
            headers = parser.ReadFields() ?? [];
            while (!parser.EndOfData)
            {
                if (cells.Count >= maxRows) throw new FormatException("Source exceeds the bounded row limit.");
                var fields = parser.ReadFields() ?? [];
                if (fields.Length != headers.Length) throw new FormatException("CSV column count mismatch.");
                cells.Add(fields);
            }
        }
        else if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new FormatException("Workbook has no worksheet.");
            var count = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
            if (last > maxRows + 1) throw new FormatException("Source exceeds the bounded row limit.");
            headers = sheet.Row(1).Cells(1, Math.Max(1, count)).Select(x => SafeCell(x)).ToArray();
            for (var i = 2; i <= last; i++)
                cells.Add(sheet.Row(i).Cells(1, count).Select(x => SafeCell(x)).ToArray());
        }
        else throw new FormatException("Only CSV/XLSX are supported.");
        headers = headers.Select(x => x.Trim().TrimStart('\uFEFF')).ToArray();
        if (headers.Length is < 1 or > 32 || headers.Any(string.IsNullOrWhiteSpace) ||
            headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new FormatException("Headers must be unique, non-empty and at most 32 columns.");
        if (mapping.Count is < 6 or > 10 ||
            CanonicalFields.Take(6).Any(required => !mapping.ContainsKey(required)) ||
            mapping.Keys.Any(key => !CanonicalFields.Contains(key, StringComparer.OrdinalIgnoreCase)))
            throw new FormatException("Required mapped fields are missing or unsupported.");
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in mapping)
        {
            var index = Array.FindIndex(headers, x => string.Equals(x, pair.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index < 0 || !positions.TryAdd(pair.Key, index))
                throw new FormatException("Invalid mapped column.");
        }
        if (positions.Values.Distinct().Count() != positions.Count)
            throw new FormatException("Mapped columns must be distinct.");
        string Cell(string[] values, string key) => positions.TryGetValue(key, out var i) && i < values.Length
            ? values[i].Trim() : string.Empty;
        return cells.Select((x, i) => new ImportRow(i + 2, Cell(x, "SourceKey"),
            Cell(x, "StudentCode"), Cell(x, "FullName"), Cell(x, "CampusId"),
            long.TryParse(Cell(x, "AcademicBatchId"), out var batchId) ? batchId : 0,
            Cell(x, "RollNo"), Cell(x, "DateOfBirth"),
            Cell(x, "GuardianName"), Cell(x, "GuardianPhone"), Cell(x, "GuardianSourceKey"))).ToList();
    }

    private static string SafeCell(IXLCell cell) => cell.HasFormula
        ? throw new FormatException("Spreadsheet formulas are not accepted.")
        : cell.GetFormattedString().Trim();

    private sealed record ImportRow(int RowNumber, string SourceKey, string StudentCode,
        string FullName, string CampusId, long BatchId, string RollNo, string DateOfBirth,
        string GuardianName, string GuardianPhone, string GuardianSourceKey);
    private sealed class ImportConflictException(string message) : Exception(message);
}
