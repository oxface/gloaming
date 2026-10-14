using ModulithFoundry.Tests.Infrastructure;
using Xunit.Sdk;
using Xunit.v3;

// Share the container, while each test creates its own database.
// Limit concurrent tests to keep database and worker resource use bounded.
[assembly: AssemblyFixture(typeof(PostgreSqlFixture))]
[assembly: Parallelization(Mode = ParallelMode.All, MaxThreads = 2)]
