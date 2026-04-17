using System.Text;
using RelayApp.Core.Adapters;
using RelayApp.Core.Models;

namespace RelayApp.Core.Protocol;

public static class RelayPromptBuilder
{
    public const string HandoffStartMarker = "===DAD_HANDOFF_START===";
    public const string HandoffEndMarker = "===DAD_HANDOFF_END===";

    public static string BuildTurnPrompt(RelayTurnContext context)
    {
        var target = GetPeer(context.SourceSide);
        var sourceValue = ToProtocolValue(context.SourceSide);
        var targetValue = ToProtocolValue(target);

        return $$"""
        This is a relay transport compliance turn, not a normal repo conversation.
        You are the {{sourceValue}} side of a relay session.
        The task is already provided below. Do not ask for another task. Do not ask the user to continue.
        When you finish, print the handoff using this exact boundary format:

        {{HandoffStartMarker}}
        {
          "type": "dad_handoff",
          "version": 1,
          "source": "{{sourceValue}}",
          "target": "{{targetValue}}",
          "session_id": "{{context.SessionId}}",
          "turn": {{context.TurnNumber}},
          "ready": true,
          "prompt": "Exact prompt for the other side.",
          "summary": ["Short checkpoint."],
          "requires_human": false,
          "reason": "",
          "created_at": "ISO-8601 timestamp with offset"
        }
        {{HandoffEndMarker}}

        Example valid output:
        {{HandoffStartMarker}}
        {
          "type": "dad_handoff",
          "version": 1,
          "source": "{{sourceValue}}",
          "target": "{{targetValue}}",
          "session_id": "{{context.SessionId}}",
          "turn": {{context.TurnNumber}},
          "ready": true,
          "prompt": "Acknowledge the relay test and return one more minimal dad_handoff JSON object.",
          "summary": ["Relay transport confirmed."],
          "requires_human": false,
          "reason": "",
          "created_at": "2026-04-16T03:00:00+09:00"
        }
        {{HandoffEndMarker}}

        Rules:
        - Keep any reasoning or tool use above {{HandoffStartMarker}}.
        - Between the markers, output only one JSON handoff object.
        - Do not print extra text after {{HandoffEndMarker}}.
        - Keep source, target, session_id, and turn consistent with this turn.
        - If automatic relay can continue, set ready=true and provide the exact next prompt.
        - If automatic relay cannot continue safely, set ready=false or requires_human=true and explain why in reason.
        - summary must contain 1 to 10 short strings.
        - created_at must include a UTC offset.
        - Never output prose like "send the relay task when ready" inside the marker block.
        - Never describe what you will do next inside the marker block; fill the JSON fields instead.

        Task:
        {{context.Prompt}}
        """;
    }

    public static string BuildRepairPrompt(RelayRepairContext context)
    {
        var target = GetPeer(context.SourceSide);
        var sourceValue = ToProtocolValue(context.SourceSide);
        var targetValue = ToProtocolValue(target);
        var builder = new StringBuilder();
        builder.AppendLine("Your previous reply did not contain a valid bounded handoff block.");
        builder.AppendLine("This is a strict repair turn.");
        builder.AppendLine("Return exactly one bounded handoff block now:");
        builder.AppendLine(HandoffStartMarker);
        builder.AppendLine("{ ... one valid dad_handoff JSON object ... }");
        builder.AppendLine(HandoffEndMarker);
        builder.AppendLine("Do not add commentary after the end marker.");
        builder.AppendLine("Do not ask for another task.");
        builder.AppendLine($"Keep type=\"dad_handoff\", version=1, source=\"{sourceValue}\", target=\"{targetValue}\", session_id=\"{context.SessionId}\", turn={context.TurnNumber}.");
        builder.AppendLine("If automatic relay cannot continue safely, set ready=false or requires_human=true and explain why in reason.");
        builder.AppendLine("Use summary as a short string array. Use created_at with an ISO-8601 timestamp and UTC offset.");
        builder.AppendLine();
        builder.AppendLine("Schema:");
        builder.AppendLine("type, version, source, target, session_id, turn, ready, prompt, summary, requires_human, reason, created_at.");
        builder.AppendLine();
        builder.AppendLine("Original task:");
        builder.AppendLine(context.OriginalPrompt);
        builder.AppendLine();
        builder.AppendLine("Previous invalid output:");
        builder.AppendLine(context.OriginalOutput);

        return builder.ToString().TrimEnd();
    }

    public static string BuildInteractiveTurnPrompt(RelayTurnContext context)
    {
        var target = GetPeer(context.SourceSide);
        var sourceValue = ToProtocolValue(context.SourceSide);
        var targetValue = ToProtocolValue(target);

        return $$"""
        This is an interactive relay transport turn.
        You are the {{sourceValue}} side.
        Do not ask for a new task.
        When you finish, print the handoff using this exact boundary format:

        {{HandoffStartMarker}}
        {
          "type": "dad_handoff",
          "version": 1,
          "source": "{{sourceValue}}",
          "target": "{{targetValue}}",
          "session_id": "{{context.SessionId}}",
          "turn": {{context.TurnNumber}},
          "ready": true,
          "prompt": "Exact prompt for the other side.",
          "summary": ["Short checkpoint."],
          "requires_human": false,
          "reason": "",
          "created_at": "ISO-8601 timestamp with offset"
        }
        {{HandoffEndMarker}}

        Rules:
        - Keep any reasoning or tool use above the start marker.
        - Between the markers, output only one JSON handoff object.
        - Do not print extra text after the end marker.
        - If you cannot continue safely, set ready=false or requires_human=true and explain why in reason.

        Task:
        {{context.Prompt}}
        """;
    }

    public static string BuildInteractiveRepairPrompt(RelayRepairContext context)
    {
        var target = GetPeer(context.SourceSide);
        var sourceValue = ToProtocolValue(context.SourceSide);
        var targetValue = ToProtocolValue(target);

        return $$"""
        Your previous interactive reply did not end with a valid relay handoff.
        Repair it now.

        Output exactly this boundary format:
        {{HandoffStartMarker}}
        { ... one valid dad_handoff JSON object ... }
        {{HandoffEndMarker}}

        Keep:
        - type="dad_handoff"
        - version=1
        - source="{{sourceValue}}"
        - target="{{targetValue}}"
        - session_id="{{context.SessionId}}"
        - turn={{context.TurnNumber}}

        Do not add commentary after {{HandoffEndMarker}}.

        Original task:
        {{context.OriginalPrompt}}

        Previous invalid output:
        {{context.OriginalOutput}}
        """;
    }

    private static RelaySide GetPeer(RelaySide side) => side == RelaySide.Codex ? RelaySide.Claude : RelaySide.Codex;

    private static string ToProtocolValue(RelaySide side) => side.ToString().ToLowerInvariant();
}
