using Microsoft.AspNetCore.Authorization;

namespace EduOS.App.Authorization;

public sealed record ModuleAccessRequirement(string ModuleCode) : IAuthorizationRequirement;
