//using AutoMapper;
//using EduOS.Core.Common;
//using EduOS.Core.DTOs.Student;
//using EduOS.Core.DTOs.Students;
//using EduOS.Core.Entities.Students;
//using EduOS.Core.Interfaces;
//using EduOS.Core.Interfaces.IRepositories;
//using EduOS.Core.Interfaces.IServices;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Caching.Distributed;
//using Microsoft.Extensions.Logging;

//namespace EduOS.Service.Services.Students
//{
//    public class StudentService : BaseService, IStudentService
//    {
//        private readonly IStudentRepository _studentRepository;
//        private readonly IAdmissionRepository _admissionRepository;
//        private readonly IEnrollmentRepository _enrollmentRepository;
//        private const string CACHE_PREFIX = "students";

//        public StudentService(
//            IUnitOfWork unitOfWork,
//            ICurrentUserService currentUser,
//            ILogger<BaseService> logger,
//            IMapper mapper,
//            IDistributedCache cache,
//            IStudentRepository studentRepository,
//            IAdmissionRepository admissionRepository,
//            IEnrollmentRepository enrollmentRepository)
//            : base(unitOfWork, currentUser, logger, mapper, cache)
//        {
//            _studentRepository = studentRepository;
//            _admissionRepository = admissionRepository;
//            _enrollmentRepository = enrollmentRepository;
//        }

//        public async Task<ApiResponse<StudentDto>> CreateAsync(StudentCreateDto dto)
//        {
//            try
//            {
//                await BeginTransactionAsync();

//                // Complex validation: Check admission status
//                var admission = await _admissionRepository.GetByIdAsync(dto.AdmissionId);
//                if (admission == null || admission.Status != "Approved")
//                {
//                    await RollbackTransactionAsync();
//                    return ApiResponse<StudentDto>.ErrorResponse("Admission not approved", 400);
//                }

//                // Check duplicate student code
//                var exists = await _studentRepository.AnyAsync(
//                    s => s.StudentCode == dto.StudentCode && s.TenantId == _currentUser.TenantId);

//                if (exists)
//                {
//                    await RollbackTransactionAsync();
//                    return ApiResponse<StudentDto>.ErrorResponse("Student code already exists", 400);
//                }

//                // Create student
//                var student = _mapper.Map<Student>(dto);
//                SetAuditFieldsCreate(student);

//                await _studentRepository.AddAsync(student);

//                // Create enrollment
//                var enrollment = new Enrollment
//                {
//                    StudentId = student.Id,
//                    AcademicYearId = dto.AcademicYearId,
//                    ClassId = dto.ClassId,
//                    SectionId = dto.SectionId,
//                    Roll = dto.Roll,
//                    TenantId = _currentUser.TenantId,
//                    CreatedBy = _currentUser.UserId,
//                    CreatedAt = DateTime.UtcNow,
//                    IsActive = true
//                };

//                await _enrollmentRepository.AddAsync(enrollment);

//                // Update admission status
//                admission.Status = "Enrolled";
//                _admissionRepository.Update(admission);

//                await _unitOfWork.SaveChangesAsync();
//                await CommitTransactionAsync();

//                // Invalidate cache
//                await RemovePatternCacheAsync($"{CACHE_PREFIX}:list:{_currentUser.TenantId}:*");

//                LogEntityCreated(student, student.Id);

//                var resultDto = _mapper.Map<StudentDto>(student);
//                return ApiResponse<StudentDto>.SuccessResponse(resultDto, "Student enrolled successfully");
//            }
//            catch (Exception ex)
//            {
//                await RollbackTransactionAsync();
//                LogError("Error enrolling student", ex);
//                return ApiResponse<StudentDto>.ErrorResponse("Failed to enroll student", 500);
//            }
//        }

//        public async Task<ApiResponse<StudentDto>> GetByIdAsync(int id)
//        {
//            try
//            {
//                var cacheKey = $"{CACHE_PREFIX}:id:{_currentUser.TenantId}:{id}";

//                var cached = await GetFromCacheAsync<StudentDto>(cacheKey);
//                if (cached != null)
//                {
//                    return ApiResponse<StudentDto>.SuccessResponse(cached);
//                }

//                var entity = await _studentRepository.GetByIdAsync(id);

//                if (entity == null || entity.TenantId != _currentUser.TenantId)
//                    return ApiResponse<StudentDto>.ErrorResponse("Student not found", 404);

//                var dto = _mapper.Map<StudentDto>(entity);

//                await SetCacheAsync(cacheKey, dto, TimeSpan.FromMinutes(10));

//                return ApiResponse<StudentDto>.SuccessResponse(dto);
//            }
//            catch (Exception ex)
//            {
//                LogError("Error getting student {Id}", ex, id);
//                return ApiResponse<StudentDto>.ErrorResponse("Failed to get student", 500);
//            }
//        }

//        public async Task<ApiResponse<PagedResult<StudentDto>>> GetAllAsync(StudentListFilterDto filter)
//        {
//            try
//            {
//                var cacheKey = $"{CACHE_PREFIX}:list:{_currentUser.TenantId}:{filter.Page}:{filter.PageSize}";

//                var cachedResult = await GetFromCacheAsync<PagedResult<StudentDto>>(cacheKey);
//                if (cachedResult != null)
//                {
//                    return ApiResponse<PagedResult<StudentDto>>.SuccessResponse(cachedResult);
//                }

//                var query = _studentRepository.GetQueryable()
//                    .Where(s => s.TenantId == _currentUser.TenantId);

//                if (!string.IsNullOrEmpty(filter.SearchTerm))
//                    query = query.Where(s => s.FullName.Contains(filter.SearchTerm));

//                if (filter.ClassId.HasValue)
//                    query = query.Where(s => s.ClassId == filter.ClassId.Value);

//                var totalCount = await query.CountAsync();
//                var items = await query
//                    .OrderByDescending(s => s.CreatedAt)
//                    .Skip((filter.Page - 1) * filter.PageSize)
//                    .Take(filter.PageSize)
//                    .ToListAsync();

//                var dtos = _mapper.Map<List<StudentDto>>(items);
//                var result = new PagedResult<StudentDto>
//                {
//                    Items = dtos,
//                    TotalCount = totalCount,
//                    Page = filter.Page,
//                    PageSize = filter.PageSize
//                };

//                await SetCacheAsync(cacheKey, result, TimeSpan.FromMinutes(5));

//                return ApiResponse<PagedResult<StudentDto>>.SuccessResponse(result);
//            }
//            catch (Exception ex)
//            {
//                LogError("Error fetching students", ex);
//                return ApiResponse<PagedResult<StudentDto>>.ErrorResponse("Failed to fetch students", 500);
//            }
//        }

//        public async Task<ApiResponse<bool>> DeleteAsync(int id)
//        {
//            try
//            {
//                await BeginTransactionAsync();

//                var entity = await _studentRepository.GetByIdAsync(id);

//                if (entity == null || entity.TenantId != _currentUser.TenantId)
//                {
//                    await RollbackTransactionAsync();
//                    return ApiResponse<bool>.ErrorResponse("Student not found", 404);
//                }

//                entity.IsDeleted = true;
//                SetAuditFieldsUpdate(entity);

//                _studentRepository.Update(entity);
//                await _unitOfWork.SaveChangesAsync();
//                await CommitTransactionAsync();

//                await RemoveCacheAsync($"{CACHE_PREFIX}:id:{_currentUser.TenantId}:{id}");
//                await RemovePatternCacheAsync($"{CACHE_PREFIX}:list:{_currentUser.TenantId}:*");

//                LogEntityDeleted(entity, entity.Id);

//                return ApiResponse<bool>.SuccessResponse(true, "Student deleted successfully");
//            }
//            catch (Exception ex)
//            {
//                await RollbackTransactionAsync();
//                LogError("Error deleting student {Id}", ex, id);
//                return ApiResponse<bool>.ErrorResponse("Failed to delete student", 500);
//            }
//        }
//    }
//}
