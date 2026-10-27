using Microsoft.Testing.Platform.ServerMode.Client;

namespace StudioUtil.Testing;

internal static class TestUpdateMapper
{
    public static TestNodeUpdate Map(MtpTestNodeUpdate update) => new()
    {
        Uid = update.Uid, DisplayName = update.DisplayName, NodeType = update.NodeType,
        ClassName = update.Node.TryGetValue("location.type", out var type) ? type as string : null,
        FilePath = update.FilePath, LineStart = update.LineStart, ExecutionState = update.ExecutionState,
        DurationInMilliseconds = update.DurationInMilliseconds, ErrorMessage = update.ErrorMessage,
        ErrorStackTrace = update.ErrorStackTrace, StandardOutput = update.StandardOutput, StandardError = update.StandardError
    };
}
