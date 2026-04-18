using CodexClaudeRelay.Core.Models;

namespace CodexClaudeRelay.Core.Protocol;

public static class TurnPacketAdapter
{
    public static TurnPacket FromHandoffEnvelope(HandoffEnvelope env)
    {
        ArgumentNullException.ThrowIfNull(env);

        return new TurnPacket
        {
            From = env.Source,
            Turn = env.Turn,
            SessionId = env.SessionId,
            Handoff = new TurnHandoff
            {
                CloseoutKind = string.IsNullOrWhiteSpace(env.CloseoutKind)
                    ? CloseoutKind.PeerHandoff
                    : env.CloseoutKind,
                NextTask = env.Prompt,
                Context = env.Reason,
                ReadyForPeerVerification = env.Ready,
                SuggestDone = env.SuggestDone,
                DoneReason = env.DoneReason,
            },
            PeerReview = new PeerReview
            {
                CheckpointResults = env.CheckpointResults,
            },
        };
    }
}
