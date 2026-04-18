namespace CodexClaudeRelay.Core.Protocol;

/// <summary>
/// Pure helper that composes the recovery_resume preamble prepended to the
/// next turn's pending prompt. Host may replace or extend the preamble by
/// injecting the full .prompts/04-session-recovery-resume.md body after the
/// broker emits session.recovery_resume.
/// </summary>
public static class RecoveryResumePromptBuilder
{
    public const string Preamble =
        "[recovery_resume] 세션 재개 — 이전 턴이 문맥 오버플로/중단으로 종료됐습니다. " +
        ".prompts/04-session-recovery-resume.md 절차를 따르세요: " +
        "state.json + 최신 turn-{N}.yaml 확인, 누락 아티팩트 복구 후 다음 턴 진행. " +
        "다음 패킷의 my_work.continued_from_resume 를 true 로 표기하세요.";

    public static string Compose(string originalPrompt)
    {
        if (string.IsNullOrWhiteSpace(originalPrompt))
        {
            return Preamble;
        }

        return $"{Preamble}\n\n---\n\n{originalPrompt}";
    }
}
