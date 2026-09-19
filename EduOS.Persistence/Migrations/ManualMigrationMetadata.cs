using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913235500_NormalizeEventOrganizerId")]
public partial class NormalizeEventOrganizerId;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260914001000_NormalizeSystemActorIdentifiers")]
public partial class NormalizeSystemActorIdentifiers;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260914002000_NormalizeVisitorMeetingPersonId")]
public partial class NormalizeVisitorMeetingPersonId;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260914003000_NormalizeSurveyIdentifiers")]
public partial class NormalizeSurveyIdentifiers;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260914004000_NormalizeSystemMetadataIdentifiers")]
public partial class NormalizeSystemMetadataIdentifiers;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260914005000_NormalizeRemainingMappedIdentifiers")]
public partial class NormalizeRemainingMappedIdentifiers;
