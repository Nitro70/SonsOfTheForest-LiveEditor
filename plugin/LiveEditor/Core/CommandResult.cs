using System.Text.Json.Nodes;

namespace LiveEditor.Core;

public abstract record CommandResult
{
    public static CommandResult Ok(JsonNode? result = null) => new CommandOk(result);

    public static CommandResult Error(string code, string message, JsonNode? data = null) =>
        new CommandError(code, message, data);
}

public sealed record CommandOk(JsonNode? Result) : CommandResult;

public sealed record CommandError(string Code, string Message, JsonNode? Data = null) : CommandResult;

public static class ErrorCodes
{
    public const string Auth = "E_AUTH";
    public const string Protocol = "E_PROTOCOL";
    public const string BadArgs = "E_BAD_ARGS";
    public const string UnknownCmd = "E_UNKNOWN_CMD";
    public const string UnknownItem = "E_UNKNOWN_ITEM";
    public const string NotInWorld = "E_NOT_IN_WORLD";
    public const string DbNotReady = "E_DB_NOT_READY";
    public const string CheatsDisabled = "E_CHEATS_DISABLED";
    public const string ExecFailed = "E_EXEC_FAILED";
    public const string Timeout = "E_TIMEOUT";
    public const string Unsupported = "E_UNSUPPORTED";
    public const string Busy = "E_BUSY";
}
