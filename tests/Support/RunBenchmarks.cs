using System.Reflection;
using BenchmarkDotNet.Running;

var summaries = BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args).ToArray();
if (args.Any(argument => argument is "--list" or "--help" or "--version"))
    return 0;

return
    summaries.Length > 0
    && summaries.All(summary =>
        !summary.HasCriticalValidationErrors
        && summary.Reports.Length > 0
        && summary.Reports.All(report => report.Success && report.ResultStatistics is not null)
    )
    ? 0
    : 1;
