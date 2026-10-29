using System.Runtime.CompilerServices;
using System.Text.Json;
using Gloaming.Evaluations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace EvaluationTests;

public sealed partial class RunnerTests
{
    [Fact]
    public async Task NativeEvaluator_UsesConsumerProjectionAndSharedJudgeBudget_WithoutAnAgent()
    {
        var calls = 0;
        var native = new ProjectedJudge();
        var suite = Suite() with
        {
            Checks =
            [
                .. Suite().Checks,
                new(
                    "native-judge",
                    "v1",
                    ["Projected"],
                    async (observation, execution, token) =>
                    {
                        using var judge = execution.WrapChatClient(
                            new CountingClient(() => calls++)
                        );
                        return await native.EvaluateAsync(
                            [
                                new(
                                    ChatRole.User,
                                    observation.Input.ToString(
                                        System.Globalization.CultureInfo.InvariantCulture
                                    )
                                ),
                            ],
                            new ChatResponse(
                                new ChatMessage(
                                    ChatRole.Assistant,
                                    observation.Actual.ToString(
                                        System.Globalization.CultureInfo.InvariantCulture
                                    )
                                )
                            ),
                            new ChatConfiguration(judge),
                            cancellationToken: token
                        );
                    },
                    IsJudge: true
                ),
            ],
        };
        var report = await EvaluationRunner.RunAsync(
            suite,
            [Target("ordinary-code")],
            new() { IncludeJudges = true, MaxModelCalls = 1 },
            Token
        );
        Assert.True(report.Passed);
        Assert.Equal(1, calls);
        Assert.Equal(1, report.ModelCalls);
    }

    private sealed class ProjectedJudge : IEvaluator
    {
        public IReadOnlyCollection<string> EvaluationMetricNames => ["Projected"];

        public async ValueTask<EvaluationResult> EvaluateAsync(
            IEnumerable<ChatMessage> messages,
            ChatResponse modelResponse,
            ChatConfiguration? chatConfiguration = null,
            IEnumerable<EvaluationContext>? additionalContext = null,
            CancellationToken cancellationToken = default
        )
        {
            Assert.Equal("1", Assert.Single(messages).Text);
            Assert.Equal("1", modelResponse.Text);
            await chatConfiguration!.ChatClient.GetResponseAsync(
                [],
                cancellationToken: cancellationToken
            );
            return await Metrics(Boolean("Projected", true));
        }
    }

    [Fact]
    public async Task SharedBudget_GenerationStreamingAndJudge_AllCountBeforeTransport()
    {
        var transportCalls = 0;
        var target = Target(
            "tool-loop",
            async (input, execution, token) =>
            {
                using var client = execution.WrapChatClient(
                    new CountingClient(() => Interlocked.Increment(ref transportCalls))
                );
                await client.GetResponseAsync([], cancellationToken: token);
                await foreach (
                    var _ in client.GetStreamingResponseAsync([], cancellationToken: token)
                ) { }

                execution.RecordArtifact("count", JsonSerializer.SerializeToElement(2));
                return input;
            }
        );
        var judge = new EvaluationCheck<int, int>(
            "judge",
            "judge-v1",
            ["Judged"],
            async (_, execution, token) =>
            {
                using var client = execution.WrapChatClient(
                    new CountingClient(() => Interlocked.Increment(ref transportCalls))
                );
                await client.GetResponseAsync([], cancellationToken: token);
                return (await Metrics(Boolean("Judged", true)));
            },
            IsJudge: true
        );
        var suite = Suite() with { Checks = [.. Suite().Checks, judge] };
        var report = await EvaluationRunner.RunAsync(
            suite,
            [target],
            new() { IncludeJudges = true, MaxModelCalls = 2 },
            Token
        );
        Assert.Equal(2, transportCalls);
        Assert.Equal(2, report.ModelCalls);
        Assert.Equal(EvaluationStatus.CallLimitReached, Assert.Single(report.Attempts).Status);
        Assert.DoesNotContain("Judged", report.Attempts[0].Metrics.Metrics.Keys);

        transportCalls = 0;
        var withoutJudge = await EvaluationRunner.RunAsync(
            suite,
            [target],
            new() { MaxModelCalls = 2 },
            Token
        );
        Assert.True(withoutJudge.Passed);
        Assert.DoesNotContain(withoutJudge.Checks, x => x.IsJudge);
        Assert.Equal(2, transportCalls);
        transportCalls = 0;
        var withJudge = await EvaluationRunner.RunAsync(
            suite,
            [target],
            new() { IncludeJudges = true, MaxModelCalls = 3 },
            Token
        );
        Assert.True(withJudge.Passed);
        Assert.Equal(3, transportCalls);
    }

    [Fact]
    public async Task ConcurrentBudget_NeverAdmitsMoreThanCap_AndSwallowingLimitCannotPass()
    {
        var transportCalls = 0;
        var target = Target(
            "parallel",
            async (input, execution, token) =>
            {
                using var client = execution.WrapChatClient(
                    new CountingClient(() => Interlocked.Increment(ref transportCalls))
                );
                try
                {
                    await client.GetResponseAsync([], cancellationToken: token);
                }
                catch (EvaluationCallLimitException) { }

                return input;
            }
        );
        var report = await EvaluationRunner.RunAsync(
            Suite(),
            [target],
            new()
            {
                Repetitions = 12,
                MaxConcurrency = 6,
                MaxModelCalls = 3,
            },
            Token
        );
        Assert.Equal(3, transportCalls);
        Assert.Equal(3, report.ModelCalls);
        Assert.Equal(3, report.Attempts.Sum(x => x.ModelCalls));
        Assert.Equal(9, report.Attempts.Count(x => x.Status == EvaluationStatus.CallLimitReached));
        Assert.Equal(3, report.Attempts.Count(x => x.Status == EvaluationStatus.Passed));
    }

    [Fact]
    public async Task DefaultBudget_ProhibitsTransport_AndArtifactsOutliveTheirJsonDocument()
    {
        var calls = 0;
        var report = await EvaluationRunner.RunAsync(
            Suite(),
            [
                Target(
                    "offline",
                    async (input, execution, token) =>
                    {
                        using (var document = JsonDocument.Parse("{\"version\":2}"))
                            execution.RecordArtifact("proof", document.RootElement);

                        using var client = execution.WrapChatClient(
                            new CountingClient(() => calls++)
                        );
                        await client.GetResponseAsync([], cancellationToken: token);
                        return input;
                    }
                ),
            ],
            cancellationToken: Token
        );
        Assert.Equal(0, calls);
        Assert.Equal(2, report.Attempts[0].Artifacts["proof"].GetProperty("version").GetInt32());
        Assert.Equal(EvaluationStatus.CallLimitReached, report.Attempts[0].Status);
    }

    private sealed class CountingClient(Action called) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            called();
            return Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "fixture"))
            );
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            foreach (
                var update in (
                    await GetResponseAsync(messages, options, cancellationToken)
                ).ToChatResponseUpdates()
            )
                yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
