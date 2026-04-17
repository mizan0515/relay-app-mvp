using RelayApp.Core.Models;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace RelayApp.Core.Policy;

public static class RelayApprovalPolicy
{
    public static string? BuildPolicyKey(string method, string category, JsonElement payload)
    {
        if (string.Equals(method, "item/commandExecution/requestApproval", StringComparison.Ordinal))
        {
            var command = TryReadString(payload, "command");
            if (string.IsNullOrWhiteSpace(command))
            {
                return null;
            }

            var normalized = NormalizeCommandForInspection(command);
            return category switch
            {
                "git" => "git:read",
                "git-add" => "git:add",
                "git-commit" => "git:commit",
                "git-push" => BuildGitPushPolicyKey(normalized),
                "pr" => BuildPullRequestPolicyKey(normalized),
                "read" => $"read:{normalized}",
                "shell" => $"shell:{normalized}",
                "command" => $"command:{normalized}",
                _ => $"command:{category}:{normalized}"
            };
        }

        return method switch
        {
            "item/fileChange/requestApproval" => BuildFileChangePolicyKey(payload),
            "item/permissions/requestApproval" => BuildPermissionsPolicyKey(payload),
            _ => null
        };
    }

    public static string BuildToolReviewPolicyKey(string category, string? title)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? "unknown"
            : Regex.Replace(ExtractToolIdentifier(title).ToLowerInvariant(), "\\s+", "-");
        return $"{category}:review:{normalizedTitle}";
    }

    public static string GetToolRiskLevel(string category) => category switch
    {
        "mcp" => "high",
        "web" => "medium",
        _ => "medium"
    };

    public static bool TryResolveSessionDecision(
        RelaySessionState state,
        RelayPendingApproval pendingApproval,
        out RelayApprovalDecision decision,
        out RelaySessionApprovalRule? matchedRule)
    {
        decision = default;
        matchedRule = null;

        if (string.IsNullOrWhiteSpace(pendingApproval.PolicyKey) || state.SessionApprovalRules.Count == 0)
        {
            return false;
        }

        matchedRule = state.SessionApprovalRules
            .LastOrDefault(rule => string.Equals(rule.PolicyKey, pendingApproval.PolicyKey, StringComparison.Ordinal));
        if (matchedRule is null)
        {
            return false;
        }

        decision = matchedRule.Decision;
        return true;
    }

    public static string ClassifyCommandCategory(JsonElement payload)
    {
        var command = TryReadString(payload, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return "command";
        }

        return ClassifyCommandCategory(command);
    }

    public static string ClassifyCommandCategory(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "command";
        }

        var trimmed = command.Trim();

        // Codex on Windows wraps every command in `"...\powershell.exe" -Command '<inner>'`
        // (and similar with pwsh / `cmd /c`). Unwrap once so the inner command lands in the
        // git/pr classification branches instead of always being called "shell".
        if (TryUnwrapShellInvocation(trimmed, out var inner))
        {
            var innerCategory = ClassifyCommandCategory(inner);
            return innerCategory == "command" ? "shell" : innerCategory;
        }

        if (trimmed.StartsWith("gh pr create", StringComparison.OrdinalIgnoreCase))
        {
            return "pr";
        }

        // `git -c key=value <sub>` and `git -C <path> <sub>` are common on Windows because of
        // safe.directory overrides. Strip those option pairs so the subcommand check still works.
        var normalizedGit = NormalizeGitPrefixFlags(trimmed);

        if (normalizedGit.StartsWith("git commit", StringComparison.OrdinalIgnoreCase))
        {
            return "git-commit";
        }

        if (normalizedGit.StartsWith("git add", StringComparison.OrdinalIgnoreCase))
        {
            return "git-add";
        }

        if (normalizedGit.StartsWith("git push", StringComparison.OrdinalIgnoreCase))
        {
            return "git-push";
        }

        if (normalizedGit.StartsWith("git ", StringComparison.OrdinalIgnoreCase))
        {
            return "git";
        }

        if (IsReadCommand(trimmed))
        {
            return "read";
        }

        if (trimmed.StartsWith("powershell", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("pwsh", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("cmd /c", StringComparison.OrdinalIgnoreCase))
        {
            return "shell";
        }

        return "command";
    }

    // Codex on Windows wraps file reads as `powershell.exe -Command "Get-Content -Raw '<path>'"`.
    // Once TryUnwrapShellInvocation has peeled the wrapper, recognise common read-only inspection
    // primitives (Get-Content / cat / type / Select-String) so the broker sees them as "read"
    // instead of generic "shell".
    private static readonly Regex ReadCommandRegex = new(
        "^(?:get-content(?:\\s|$)|gc(?:\\s|$)|cat(?:\\s|$)|type(?:\\s|$)|select-string(?:\\s|$)|sls(?:\\s|$))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsReadCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var trimmed = command.Trim();
        if (!ReadCommandRegex.IsMatch(trimmed))
        {
            return false;
        }

        // Reject piped/chained commands — a `Get-Content foo | Remove-Item` style chain is not a
        // pure read. Treat anything with `|`, `;`, `&&`, `||`, or `>` as non-read.
        foreach (var ch in trimmed)
        {
            if (ch == '|' || ch == ';' || ch == '>' || ch == '&')
            {
                return false;
            }
        }

        return true;
    }

    private static readonly Regex ShellWrapperRegex = new(
        "^(?:\"?(?:[A-Za-z]:\\\\[^\"]*?\\\\)?(?:powershell(?:\\.exe)?|pwsh(?:\\.exe)?)\"?\\s+(?:-(?:Command|c))\\s+|cmd(?:\\.exe)?\\s+/c\\s+)(?<inner>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    public static bool TryUnwrapShellInvocation(string command, out string inner)
    {
        inner = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var match = ShellWrapperRegex.Match(command.Trim());
        if (!match.Success)
        {
            return false;
        }

        inner = StripMatchingOuterQuotes(match.Groups["inner"].Value).Trim();
        return inner.Length > 0;
    }

    private static string StripMatchingOuterQuotes(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length < 2)
        {
            return trimmed;
        }

        var first = trimmed[0];
        var last = trimmed[^1];
        if ((first == '"' || first == '\'') && first == last)
        {
            return trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }

    private static string NormalizeGitPrefixFlags(string command)
    {
        if (!command.StartsWith("git ", StringComparison.OrdinalIgnoreCase) &&
            !command.Equals("git", StringComparison.OrdinalIgnoreCase))
        {
            return command;
        }

        var tokens = Regex.Split(command, "\\s+");
        if (tokens.Length <= 1)
        {
            return command;
        }

        var result = new List<string> { tokens[0] };
        var i = 1;
        while (i < tokens.Length)
        {
            var token = tokens[i];
            if ((string.Equals(token, "-c", StringComparison.Ordinal) ||
                 string.Equals(token, "-C", StringComparison.Ordinal)) &&
                i + 1 < tokens.Length)
            {
                i += 2;
                continue;
            }

            result.Add(token);
            i++;
        }

        return string.Join(' ', result);
    }

    public static string GetApprovalTitle(string category) => category switch
    {
        "git" => "Git Approval",
        "git-add" => "Git Stage Approval",
        "git-commit" => "Git Commit Approval",
        "git-push" => "Git Push Approval",
        "pr" => "Pull Request Approval",
        "shell" => "Shell Approval",
        "read" => "Read Approval",
        "dad-asset" => "DAD Asset Change Approval",
        "file-change" => "File Change Approval",
        "permissions" => "Permissions Approval",
        "mcp" => "MCP Tool Approval",
        "web" => "Web Tool Approval",
        "tool" => "Tool Approval",
        _ => "Command Approval"
    };

    public static string GetRiskLevel(string category, JsonElement payload)
    {
        return category switch
        {
            "git" => "low",
            "git-add" => "medium",
            "git-commit" => "medium",
            "git-push" => IsForceLikeGitPush(payload) || TargetsProtectedPushBranch(payload) ? "critical" : "high",
            "pr" => TargetsProtectedPullRequestBase(payload) ? "critical" : "high",
            "shell" => "high",
            "read" => "low",
            "command" => "medium",
            "file-change" => TargetsProtectedGitPath(payload) ? "critical" : "high",
            "dad-asset" => TargetsProtectedDadAssetPath(payload) ? "critical" : "high",
            "permissions" => RequestsNetworkPermission(payload) || RequestsBroadFileSystemPermission(payload) ? "critical" : "high",
            "mcp" => "high",
            "web" => "medium",
            "tool" => "medium",
            _ => "medium"
        };
    }

    public static string DescribeRiskLevel(string riskLevel) => riskLevel switch
    {
        "low" => "Risk: low.",
        "medium" => "Risk: medium.",
        "high" => "Risk: high.",
        "critical" => "Risk: critical.",
        _ => "Risk: unknown."
    };

    public static string? GetCategoryEventType(string category, string stage) => category switch
    {
        "git" => $"git.{stage}",
        "git-add" => $"git.add.{stage}",
        "git-commit" => $"git.commit.{stage}",
        "git-push" => $"git.push.{stage}",
        "pr" => $"pr.{stage}",
        "shell" => $"shell.{stage}",
        "read" => $"read.{stage}",
        "file-change" => $"file.change.{stage}",
        "dad-asset" => $"dad.asset.{stage}",
        "permissions" => $"permissions.{stage}",
        "mcp" => $"mcp.{stage}",
        "web" => $"web.{stage}",
        _ => null
    };

    public static string ClassifyToolCategory(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return "tool";
        }

        var normalized = toolName.Trim();
        if (normalized.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase))
        {
            return "mcp";
        }

        if (string.Equals(normalized, "ListMcpResourcesTool", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "ReadMcpResourceTool", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("McpResource", StringComparison.OrdinalIgnoreCase))
        {
            return "mcp";
        }

        if (string.Equals(normalized, "web_search", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "web_fetch", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("web_search", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("web_fetch", StringComparison.OrdinalIgnoreCase))
        {
            return "web";
        }

        if (string.Equals(normalized, "bash", StringComparison.OrdinalIgnoreCase))
        {
            return "shell";
        }

        if (string.Equals(normalized, "edit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "write", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "multi_edit", StringComparison.OrdinalIgnoreCase))
        {
            return "file-change";
        }

        return "tool";
    }

    public static string ClassifyCodexItemCategory(string? itemType)
    {
        if (string.IsNullOrWhiteSpace(itemType))
        {
            return "tool";
        }

        var normalized = itemType.Trim();
        if (normalized.Contains("mcp", StringComparison.OrdinalIgnoreCase))
        {
            return "mcp";
        }

        if (normalized.Contains("web", StringComparison.OrdinalIgnoreCase))
        {
            return "web";
        }

        if (normalized.Contains("command", StringComparison.OrdinalIgnoreCase))
        {
            return "shell";
        }

        if (normalized.Contains("file", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("patch", StringComparison.OrdinalIgnoreCase))
        {
            return "file-change";
        }

        return "tool";
    }

    public static string GetToolTitle(string category, string? toolName)
    {
        var normalizedTool = string.IsNullOrWhiteSpace(toolName) ? "unknown-tool" : toolName.Trim();
        return category switch
        {
            "mcp" => $"MCP Tool: {normalizedTool}",
            "web" => $"Web Tool: {normalizedTool}",
            "file-change" => $"File Tool: {normalizedTool}",
            "shell" => $"Shell Tool: {normalizedTool}",
            "tool" => $"Tool: {normalizedTool}",
            _ => GetApprovalTitle(category)
        };
    }

    public static string? DescribeToolSummary(string category, string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return null;
        }

        var normalizedTool = toolName.Trim();
        return category switch
        {
            "mcp" => $"Summary: invoke MCP tool '{normalizedTool}'.",
            "web" => $"Summary: invoke web-facing tool '{normalizedTool}'.",
            "file-change" => $"Summary: invoke file-modifying tool '{normalizedTool}'.",
            _ => null
        };
    }

    public static string? DescribeToolReviewPolicy(string category) => category switch
    {
        "mcp" => "Policy: operator review required because MCP activity is broker-audited and session-governed, but not yet broker-routed pre-execution approval.",
        "web" => "Policy: operator review required because web activity is visible to the broker but still outside broker-routed pre-execution approval.",
        _ => null
    };

    public static bool TryResolveDefaultToolReviewDecision(
        string category,
        string? title,
        string? payload,
        out RelayApprovalDecision decision,
        out string reason)
    {
        decision = default;
        reason = string.Empty;

        if (!string.Equals(category, "mcp", StringComparison.Ordinal))
        {
            return false;
        }

        var toolIdentifier = ExtractToolIdentifier(title);
        if (string.Equals(toolIdentifier, "ListMcpResourcesTool", StringComparison.OrdinalIgnoreCase))
        {
            decision = RelayApprovalDecision.ApproveOnce;
            reason = "Default MCP review policy auto-allows resource discovery because listing MCP resources is read-only.";
            return true;
        }

        if (string.Equals(toolIdentifier, "ReadMcpResourceTool", StringComparison.OrdinalIgnoreCase))
        {
            decision = RelayApprovalDecision.ApproveOnce;
            reason = "Default MCP review policy auto-allows MCP resource reads because this activity is read-only.";
            return true;
        }

        if (LooksLikeReadOnlyMcpTelemetry(payload))
        {
            decision = RelayApprovalDecision.ApproveOnce;
            reason = "Default MCP review policy auto-allows telemetry ping/status actions because they are treated as read-only capability checks.";
            return true;
        }

        return false;
    }

    public static bool TryResolveDefaultDecision(
        string method,
        RelayPendingApproval pendingApproval,
        JsonElement payload,
        out RelayApprovalDecision decision,
        out string reason)
    {
        decision = default;
        reason = string.Empty;

        if (!string.Equals(method, "item/commandExecution/requestApproval", StringComparison.Ordinal))
        {
            if (string.Equals(method, "item/permissions/requestApproval", StringComparison.Ordinal))
            {
                if (RequestsNetworkPermission(payload))
                {
                    decision = RelayApprovalDecision.Deny;
                    reason = "Default permissions policy blocks granting additional network access.";
                    return true;
                }

                if (RequestsBroadFileSystemPermission(payload))
                {
                    decision = RelayApprovalDecision.Deny;
                    reason = "Default permissions policy blocks broad filesystem access escalation.";
                    return true;
                }

                return false;
            }

            if (string.Equals(method, "item/fileChange/requestApproval", StringComparison.Ordinal))
            {
                if (TargetsProtectedGitPath(payload))
                {
                    decision = RelayApprovalDecision.Deny;
                    reason = "Default file-change policy blocks writes to protected git metadata paths.";
                    return true;
                }

                return false;
            }

            return false;
        }

        var command = TryReadString(payload, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var normalized = command.Trim();
        if (IsDestructiveGitCommand(normalized))
        {
            decision = RelayApprovalDecision.Deny;
            reason = "Default git safety policy blocks destructive git commands.";
            return true;
        }

        if (IsDestructiveShellCommand(normalized))
        {
            decision = RelayApprovalDecision.Deny;
            reason = "Default shell safety policy blocks destructive machine-level commands.";
            return true;
        }

        if (pendingApproval.Category == "git-push" && TargetsProtectedPushBranch(normalized))
        {
            decision = RelayApprovalDecision.Deny;
            reason = "Default git safety policy blocks direct pushes to protected branches.";
            return true;
        }

        if (pendingApproval.Category == "git" &&
            (normalized.StartsWith("git status", StringComparison.OrdinalIgnoreCase) ||
             normalized.StartsWith("git diff", StringComparison.OrdinalIgnoreCase) ||
             normalized.StartsWith("git log", StringComparison.OrdinalIgnoreCase)))
        {
            decision = RelayApprovalDecision.ApproveOnce;
            reason = "Default git safety policy auto-allows read-only git inspection commands.";
            return true;
        }

        if (pendingApproval.Category == "read" && IsReadCommand(normalized))
        {
            decision = RelayApprovalDecision.ApproveOnce;
            reason = "Default read safety policy auto-allows read-only file inspection commands.";
            return true;
        }

        return false;
    }

    public static string? DescribeDefaultPolicy(string method, string category, JsonElement payload)
    {
        if (string.Equals(method, "item/permissions/requestApproval", StringComparison.Ordinal))
        {
            if (RequestsNetworkPermission(payload))
            {
                return "Policy: blocked by default because this requests additional network access.";
            }

            if (RequestsBroadFileSystemPermission(payload))
            {
                return "Policy: blocked by default because this requests broad filesystem access.";
            }

            return "Policy: operator approval required for permission changes.";
        }

        if (string.Equals(method, "item/fileChange/requestApproval", StringComparison.Ordinal))
        {
            if (TargetsProtectedGitPath(payload))
            {
                return "Policy: blocked by default because this targets protected git metadata paths.";
            }

            if (TargetsProtectedDadAssetPath(payload))
            {
                return "Policy: operator approval required and risk is elevated because this targets a DAD-runtime artifact (backlog/state).";
            }

            var refined = RefineFileChangeCategory(payload);
            return refined == "dad-asset"
                ? "Policy: operator approval required for DAD asset changes (Document/dialogue/**)."
                : "Policy: operator approval required for file changes.";
        }

        if (!string.Equals(method, "item/commandExecution/requestApproval", StringComparison.Ordinal))
        {
            return null;
        }

        var command = TryReadString(payload, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var normalized = NormalizeCommandForInspection(command);
        if (IsDestructiveGitCommand(normalized))
        {
            return "Policy: blocked by default because this is a destructive git command.";
        }

        if (IsDestructiveShellCommand(normalized))
        {
            return "Policy: blocked by default because this is a destructive machine-level command.";
        }

        return category switch
        {
            "git" when normalized.StartsWith("git status", StringComparison.OrdinalIgnoreCase) ||
                        normalized.StartsWith("git diff", StringComparison.OrdinalIgnoreCase) ||
                        normalized.StartsWith("git log", StringComparison.OrdinalIgnoreCase)
                => "Policy: auto-allowed once because this is a read-only git inspection command.",
            "git-add" => "Policy: operator approval required for staging changes.",
            "git-commit" => "Policy: operator approval required for creating commits.",
            "git-push" when TargetsProtectedPushBranch(normalized)
                => "Policy: blocked by default because this push targets a protected branch.",
            "git-push" => "Policy: operator approval required for pushing to a remote.",
            "pr" when TargetsProtectedPullRequestBase(normalized)
                => "Policy: operator approval required and risk is elevated because this PR targets a protected base branch.",
            "pr" => "Policy: operator approval required for pull request creation.",
            "shell" => "Policy: operator approval required for shell execution.",
            "read" => "Policy: auto-allowed once because this is a read-only file inspection command.",
            "command" => "Policy: operator approval required for command execution.",
            _ => null
        };
    }

    public static string? DescribeCommandSummary(string category, JsonElement payload)
    {
        var command = TryReadString(payload, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        return DescribeCommandSummary(category, command);
    }

    public static string? DescribeCommandSummary(string category, string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var normalized = NormalizeCommandForInspection(command);
        return category switch
        {
            "git-commit" => BuildGitCommitSummary(normalized),
            "git-push" => BuildGitPushSummary(normalized),
            "pr" => BuildPullRequestSummary(normalized),
            "git-add" => "Summary: stage repository changes for a later commit.",
            "git" => "Summary: inspect repository state without creating new remote-visible changes.",
            "read" => "Summary: read file contents without modification.",
            _ => null
        };
    }

    public static string? DescribeFileChangeSummary(JsonElement payload)
    {
        var targetPath = TryReadFirstPath(payload);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return "Summary: apply file changes.";
        }

        return $"Summary: apply file changes targeting {targetPath}.";
    }

    public static string? DescribePermissionsSummary(JsonElement payload)
    {
        var requested = new List<string>();
        if (RequestsNetworkPermission(payload))
        {
            requested.Add("network access");
        }

        if (RequestsBroadFileSystemPermission(payload))
        {
            requested.Add("broad filesystem access");
        }

        if (requested.Count == 0)
        {
            return "Summary: request additional runtime permissions.";
        }

        return $"Summary: request {string.Join(" and ", requested)}.";
    }

    private static bool IsDestructiveGitCommand(string command) =>
        command.StartsWith("git push --force", StringComparison.OrdinalIgnoreCase) ||
        command.StartsWith("git push -f", StringComparison.OrdinalIgnoreCase) ||
        command.StartsWith("git reset --hard", StringComparison.OrdinalIgnoreCase);

    private static bool IsForceLikeGitPush(JsonElement payload)
    {
        var command = TryReadString(payload, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var normalized = NormalizeCommandForInspection(command);
        return normalized.Contains("--force", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(normalized, @"(^|\s)-f(\s|$)", RegexOptions.IgnoreCase);
    }

    private static bool TargetsProtectedPushBranch(JsonElement payload)
    {
        var command = TryReadString(payload, "command");
        return !string.IsNullOrWhiteSpace(command) && TargetsProtectedPushBranch(NormalizeCommandForInspection(command));
    }

    private static bool TargetsProtectedPushBranch(string command)
    {
        return TryExtractGitPushRemoteBranch(command, out _, out var branch) &&
               IsProtectedBranchName(branch);
    }

    private static bool TargetsProtectedPullRequestBase(JsonElement payload)
    {
        var command = TryReadString(payload, "command");
        return !string.IsNullOrWhiteSpace(command) && TargetsProtectedPullRequestBase(NormalizeCommandForInspection(command));
    }

    public static string NormalizeCommandForInspection(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var trimmed = command.Trim();
        if (TryUnwrapShellInvocation(trimmed, out var inner))
        {
            trimmed = inner.Trim();
        }

        return NormalizeGitPrefixFlags(trimmed);
    }

    private static bool TargetsProtectedPullRequestBase(string command)
    {
        TryExtractPullRequestBaseHead(command, out var baseBranch, out _);
        return IsProtectedBranchName(baseBranch);
    }

    private static bool IsDestructiveShellCommand(string command) =>
        command.StartsWith("rm -rf", StringComparison.OrdinalIgnoreCase) ||
        command.StartsWith("rm -fr", StringComparison.OrdinalIgnoreCase) ||
        (command.StartsWith("remove-item", StringComparison.OrdinalIgnoreCase) &&
         command.Contains("-recurse", StringComparison.OrdinalIgnoreCase)) ||
        command.StartsWith("del /f", StringComparison.OrdinalIgnoreCase) ||
        command.StartsWith("rmdir /s", StringComparison.OrdinalIgnoreCase) ||
        command.StartsWith("format ", StringComparison.OrdinalIgnoreCase) ||
        command.StartsWith("shutdown ", StringComparison.OrdinalIgnoreCase) ||
        command.StartsWith("stop-computer", StringComparison.OrdinalIgnoreCase);

    private static string BuildGitCommitSummary(string command)
    {
        var message = TryExtractOptionValue(command, "-m");
        return string.IsNullOrWhiteSpace(message)
            ? "Summary: create a local git commit."
            : $"Summary: create a local git commit with message \"{message}\".";
    }

    private static string BuildGitPushSummary(string command)
    {
        var summary = "Summary: push local commits to a remote branch.";
        var force = command.Contains("--force", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(command, @"(^|\s)-f(\s|$)", RegexOptions.IgnoreCase);
        TryExtractGitPushRemoteBranch(command, out var remote, out var branch);

        if (!string.IsNullOrWhiteSpace(remote) && !string.IsNullOrWhiteSpace(branch))
        {
            summary = $"Summary: push local commits to {remote}/{branch}.";
        }
        else if (!string.IsNullOrWhiteSpace(remote))
        {
            summary = $"Summary: push local commits to remote {remote}.";
        }

        if (TargetsProtectedPushBranch(command))
        {
            summary = $"{summary} Protected branch target.";
        }

        return force ? $"{summary} Force push requested." : summary;
    }

    private static string BuildPullRequestSummary(string command)
    {
        var title = TryExtractOptionValue(command, "--title");
        var baseBranch = TryExtractOptionValue(command, "--base");
        var headBranch = TryExtractOptionValue(command, "--head");

        var pieces = new List<string> { "Summary: create a pull request" };
        if (!string.IsNullOrWhiteSpace(title))
        {
            pieces.Add($"title \"{title}\"");
        }

        if (!string.IsNullOrWhiteSpace(headBranch) && !string.IsNullOrWhiteSpace(baseBranch))
        {
            pieces.Add($"from {headBranch} into {baseBranch}");
        }
        else if (!string.IsNullOrWhiteSpace(baseBranch))
        {
            pieces.Add($"into {baseBranch}");
        }
        else if (!string.IsNullOrWhiteSpace(headBranch))
        {
            pieces.Add($"from {headBranch}");
        }

        var summary = string.Join(" ", pieces) + ".";
        return TargetsProtectedPullRequestBase(command)
            ? $"{summary} Protected base branch target."
            : summary;
    }

    private static string BuildGitPushPolicyKey(string command)
    {
        if (TryExtractGitPushRemoteBranch(command, out var remote, out var branch))
        {
            return $"git:push:{remote}:{branch ?? "*"}";
        }

        return "git:push";
    }

    private static string BuildPullRequestPolicyKey(string command)
    {
        TryExtractPullRequestBaseHead(command, out var baseBranch, out var headBranch);
        if (!string.IsNullOrWhiteSpace(headBranch) || !string.IsNullOrWhiteSpace(baseBranch))
        {
            return $"pr:create:{headBranch ?? "*"}:{baseBranch ?? "*"}";
        }

        return "pr:create";
    }

    private static bool TryExtractGitPushRemoteBranch(string command, out string? remote, out string? branch)
    {
        remote = null;
        branch = null;

        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var tokens = Regex.Matches(command, "\"[^\"]*\"|'[^']*'|\\S+")
            .Select(match => TrimQuotes(match.Value))
            .ToArray();
        if (tokens.Length < 3)
        {
            return false;
        }

        var positional = new List<string>();
        for (var i = 2; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (string.IsNullOrWhiteSpace(token) || token.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            positional.Add(token);
        }

        if (positional.Count == 0)
        {
            return false;
        }

        remote = positional[0];
        if (positional.Count >= 2)
        {
            branch = NormalizeBranchName(positional[1]);
        }

        return !string.IsNullOrWhiteSpace(remote);
    }

    private static void TryExtractPullRequestBaseHead(string command, out string? baseBranch, out string? headBranch)
    {
        baseBranch = NormalizeBranchName(TryExtractOptionValue(command, "--base"));
        headBranch = NormalizeBranchName(TryExtractOptionValue(command, "--head"));
    }

    private static string? TryExtractOptionValue(string command, string option)
    {
        var match = Regex.Match(
            command,
            $@"(?:^|\s){Regex.Escape(option)}\s+(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>\S+))",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string TrimQuotes(string token) =>
        token.Length >= 2 &&
        ((token.StartsWith('"') && token.EndsWith('"')) || (token.StartsWith('\'') && token.EndsWith('\'')))
            ? token[1..^1]
            : token;

    private static string ExtractToolIdentifier(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "unknown";
        }

        var normalized = title.Trim();
        var colonIndex = normalized.LastIndexOf(':');
        if (colonIndex >= 0 && colonIndex < normalized.Length - 1)
        {
            normalized = normalized[(colonIndex + 1)..].Trim();
        }

        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }

    private static bool LooksLikeReadOnlyMcpTelemetry(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        return payload.Contains("\"action\":\"telemetry_ping\"", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("\"action\": \"telemetry_ping\"", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("\"action\":\"telemetry_status\"", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("\"action\": \"telemetry_status\"", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("\"action\":\"ping\"", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("\"action\": \"ping\"", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("\"action\":\"status\"", StringComparison.OrdinalIgnoreCase) ||
               payload.Contains("\"action\": \"status\"", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProtectedBranchName(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            return false;
        }

        var normalized = NormalizeBranchName(branch);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Equals("main", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("master", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("trunk", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("prod", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("production", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("stable", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("release/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("hotfix/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeBranchName(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            return branch;
        }

        var normalized = branch.Trim();
        if (normalized.Contains(':'))
        {
            normalized = normalized[(normalized.LastIndexOf(':') + 1)..];
        }

        const string refsHeadsPrefix = "refs/heads/";
        if (normalized.StartsWith(refsHeadsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[refsHeadsPrefix.Length..];
        }

        return normalized.Trim();
    }

    private static string? TryReadString(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out var propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return propertyElement.GetString();
    }

    private static string BuildFileChangePolicyKey(JsonElement payload)
    {
        var targetPath = TryReadFirstPath(payload);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return "file-change";
        }

        var trimmed = targetPath.Trim();
        var prefix = IsDadAssetPath(trimmed) ? "dad-asset" : "file-change";
        return $"{prefix}:{trimmed}";
    }

    private static string BuildPermissionsPolicyKey(JsonElement payload)
    {
        var parts = new List<string>();
        if (RequestsNetworkPermission(payload))
        {
            parts.Add("network");
        }

        if (RequestsBroadFileSystemPermission(payload))
        {
            parts.Add("filesystem");
        }

        return parts.Count == 0
            ? "permissions"
            : $"permissions:{string.Join("+", parts)}";
    }

    private static bool RequestsNetworkPermission(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("permissions", out var permissionsElement) ||
            permissionsElement.ValueKind != JsonValueKind.Object ||
            !permissionsElement.TryGetProperty("network", out var networkElement))
        {
            return false;
        }

        return networkElement.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(networkElement.GetString()) &&
                                    !string.Equals(networkElement.GetString(), "none", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Object => true,
            _ => false
        };
    }

    private static bool RequestsBroadFileSystemPermission(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("permissions", out var permissionsElement) ||
            permissionsElement.ValueKind != JsonValueKind.Object ||
            !permissionsElement.TryGetProperty("fileSystem", out var fileSystemElement))
        {
            return false;
        }

        if (fileSystemElement.ValueKind == JsonValueKind.String)
        {
            var value = fileSystemElement.GetString();
            return !string.IsNullOrWhiteSpace(value) &&
                   (value.Contains("danger", StringComparison.OrdinalIgnoreCase) ||
                    value.Contains("full", StringComparison.OrdinalIgnoreCase) ||
                    value.Contains("write", StringComparison.OrdinalIgnoreCase));
        }

        return fileSystemElement.ValueKind == JsonValueKind.Object;
    }

    private static bool TargetsProtectedGitPath(JsonElement payload)
    {
        var path = TryReadFirstPath(payload);
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/').Trim();
        return normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, ".git", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, ".gitmodules", StringComparison.OrdinalIgnoreCase);
    }

    // DAD-runtime-critical artifacts: backlog / state / session transcripts. Writes to these
    // paths deserve elevated risk regardless of whether they are inside a DAD workspace; when
    // combined with IsDadAssetPath() gating, these paths become the "protected" inner ring.
    public static bool IsDadAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/').Trim();
        var slash = normalized.LastIndexOf('/');
        var tail = slash < 0 ? normalized : normalized[(slash + 1)..];
        return normalized.Contains("/Document/dialogue/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Document/dialogue/", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tail, "backlog.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tail, "state.json", StringComparison.OrdinalIgnoreCase);
    }

    public static string RefineFileChangeCategory(string path)
    {
        return IsDadAssetPath(path) ? "dad-asset" : "file-change";
    }

    public static string RefineFileChangeCategory(JsonElement payload)
    {
        var path = TryReadFirstPath(payload);
        return string.IsNullOrWhiteSpace(path) ? "file-change" : RefineFileChangeCategory(path);
    }

    private static bool TargetsProtectedDadAssetPath(JsonElement payload)
    {
        var path = TryReadFirstPath(payload);
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/').Trim();
        var slash = normalized.LastIndexOf('/');
        var tail = slash < 0 ? normalized : normalized[(slash + 1)..];
        // Treat backlog and state mutations as the protected inner ring — these drive DAD
        // runtime semantics (active item, session gating). Session transcript writes are the
        // non-protected DAD-asset ring.
        return string.Equals(tail, "backlog.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tail, "state.json", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryReadFirstPath(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in new[] { "changes", "fileChanges", "paths" })
        {
            if (!payload.TryGetProperty(propertyName, out var propertyElement) || propertyElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in propertyElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    return item.GetString();
                }

                if (item.ValueKind == JsonValueKind.Object)
                {
                    foreach (var candidate in new[] { "path", "filePath", "targetPath" })
                    {
                        if (item.TryGetProperty(candidate, out var candidateElement) && candidateElement.ValueKind == JsonValueKind.String)
                        {
                            return candidateElement.GetString();
                        }
                    }
                }
            }
        }

        return null;
    }
}
