using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Gloaming.Samples.Wholesale.AppHost;

namespace Gloaming.Samples.Wholesale.RuntimeComposition.Tests;

internal static class WholesaleTestHost
{
    public static async Task<IDistributedApplicationTestingBuilder> CreateAsync(
        string[] arguments,
        CancellationToken cancellation
    )
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<AppHostMarker>(
            arguments,
            cancellation
        );
        try
        {
            builder.Configuration["DcpPublisher:RandomizePorts"] = "true";
            WholesaleContainerCleanup.Configure(builder);
            string configuration = typeof(WholesaleTestHost)
                .Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!
                .Configuration;
            // Both resources launch the same project and share the output built for this test run.
            foreach (string resource in new[] { "api", "demo-setup" })
            {
                var project = builder.CreateResourceBuilder<ProjectResource>(resource);
                string projectPath = project.Resource.GetProjectMetadata().ProjectPath;
                string[] command =
                [
                    "run",
                    "--project",
                    projectPath,
                    "--configuration",
                    configuration,
                    "--no-build",
                    "--no-launch-profile",
                    "--",
                ];
                project
                    .WithAnnotation(
                        new ExecutableAnnotation
                        {
                            Command = "dotnet",
                            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
                        }
                    )
                    .WithArgs(context =>
                    {
                        // Preserve the application arguments, including the setup mode.
                        for (int index = 0; index < command.Length; index++)
                            context.Args.Insert(index, command[index]);
                    });
            }

            return builder;
        }
        catch
        {
            await builder.DisposeAsync();
            throw;
        }
    }
}
