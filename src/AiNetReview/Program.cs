namespace AiNetReview;

using System.Text.Json;

public static class Program
{
    private const int ExitCodeInvalidInput = 2;
    private const int ExitCodeLoggingFailed = 4;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            HostLogging.Initialize(GetCommandCategory(args));
        }
        catch (Exception)
        {
            await Console.Error.WriteLineAsync(JsonSerializer.Serialize(new
            {
                code = "LOGGING_FAILED",
                message = "Host logging could not be initialized.",
            }));
            return ExitCodeLoggingFailed;
        }

        try
        {
            var message = args is { Length: > 0 }
                ? $"Unbekannter oder noch nicht implementierter Befehl '{args[0]}'."
                : "Kein Befehl angegeben. Verf\u00fcgbare Befehle: review, catalog, mcp.";

            await Console.Error.WriteLineAsync($"{{\"code\":\"INVALID_INPUT\",\"message\":\"{message}\"}}");
            return ExitCodeInvalidInput;
        }
        finally
        {
            await HostLogging.CloseAndFlushAsync();
        }
    }

    private static string GetCommandCategory(string[] args) => args.FirstOrDefault() switch
    {
        "review" => "review",
        "catalog" => "catalog",
        "mcp" => "mcp",
        null => "none",
        _ => "unknown",
    };
}
