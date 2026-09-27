namespace AiNetReview;

public static class Program
{
    private const int ExitCodeInvalidInput = 2;

    public static async Task<int> Main(string[] args)
    {
        var message = args is { Length: > 0 }
            ? $"Unbekannter oder noch nicht implementierter Befehl '{args[0]}'."
            : "Kein Befehl angegeben. Verf\u00fcgbare Befehle: review, catalog, mcp.";

        await Console.Error.WriteLineAsync($"{{\"code\":\"INVALID_INPUT\",\"message\":\"{message}\"}}");
        return ExitCodeInvalidInput;
    }
}
