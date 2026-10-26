using Xunit;

namespace EduOS.Tests.App;

// These tests share one SQL Server database. Run SQL integration classes serially
// so unrelated workflows cannot deadlock each other's setup and assertions.
[CollectionDefinition("SqlServerIntegration", DisableParallelization = true)]
public sealed class SqlServerIntegrationCollection
{
}
