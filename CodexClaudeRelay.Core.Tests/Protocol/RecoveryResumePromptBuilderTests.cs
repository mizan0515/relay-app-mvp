using CodexClaudeRelay.Core.Protocol;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Protocol;

public class RecoveryResumePromptBuilderTests
{
    [Fact]
    public void Compose_prepends_preamble_and_keeps_original_prompt()
    {
        var original = "continue refactoring HandoffEnvelope tests";

        var composed = RecoveryResumePromptBuilder.Compose(original);

        Assert.StartsWith(RecoveryResumePromptBuilder.Preamble, composed);
        Assert.Contains(original, composed);
        Assert.Contains("\n\n---\n\n", composed);
    }

    [Fact]
    public void Compose_empty_prompt_returns_preamble_only()
    {
        Assert.Equal(RecoveryResumePromptBuilder.Preamble, RecoveryResumePromptBuilder.Compose(string.Empty));
        Assert.Equal(RecoveryResumePromptBuilder.Preamble, RecoveryResumePromptBuilder.Compose("   "));
    }

    [Fact]
    public void Preamble_references_prompt_04_and_continued_from_resume_marker()
    {
        Assert.Contains("04-session-recovery-resume.md", RecoveryResumePromptBuilder.Preamble);
        Assert.Contains("continued_from_resume", RecoveryResumePromptBuilder.Preamble);
        Assert.Contains("recovery_resume", RecoveryResumePromptBuilder.Preamble);
    }
}
