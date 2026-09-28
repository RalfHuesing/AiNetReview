namespace AiNetReview.Core.Configuration;

using System;
using System.IO;
using System.Text.Json;
using AiNetReview.Core.Rules;

/// <summary>Generates the schema version 1 configuration populated with registered rule defaults.</summary>
public sealed class DefaultReviewConfigGenerator
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    private readonly RuleRegistry registry;

    public DefaultReviewConfigGenerator(RuleRegistry registry)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        if (registry.Rules.Count == 0)
        {
            throw new ArgumentException("At least one rule must be registered to generate a valid review configuration.", nameof(registry));
        }
    }

    /// <summary>Returns configuration JSON for a project-relative solution path.</summary>
    public string Generate(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        if (Path.IsPathRooted(solutionPath)
            || solutionPath.Contains('/')
            || solutionPath.Contains('\\')
            || solutionPath.Contains(':')
            || !Path.GetFileName(solutionPath).Equals(solutionPath, StringComparison.Ordinal)
            || !(Path.GetExtension(solutionPath).Equals(".sln", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(solutionPath).Equals(".slnx", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Solution must be a top-level .sln or .slnx file name.", nameof(solutionPath));
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("solution", solutionPath);
            writer.WriteString("outputDirectory", "audit-reporting");
            writer.WritePropertyName("rules");
            writer.WriteStartObject();

            foreach (var rule in registry.Rules)
            {
                var descriptor = rule.Descriptor;
                writer.WritePropertyName(descriptor.RuleId);
                writer.WriteStartObject();
                writer.WriteBoolean("enabled", descriptor.DefaultEnabled);
                foreach (var option in descriptor.Options)
                {
                    writer.WritePropertyName(option.Name);
                    option.DefaultValue.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }
}
