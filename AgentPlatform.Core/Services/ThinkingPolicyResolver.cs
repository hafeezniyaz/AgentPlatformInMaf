using System.Text.Json;
using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Core.Services;

public sealed class ThinkingPolicyResolver(IOptions<AgentPlatformOptions> options) : IThinkingPolicyResolver
{
    public const string ModeDisabled = "disabled";
    public const string ModeInterleaved = "interleaved";
    public const string ModePreserved = "preserved";
    public const string CaptureNone = "none";
    public const string CaptureSummary = "summary";
    public const string CaptureFull = "full";
    public const string CaptureOpaque = "opaque";

    private static readonly HashSet<string> Modes = new(StringComparer.OrdinalIgnoreCase)
    {
        ModeDisabled,
        ModeInterleaved,
        ModePreserved
    };

    private static readonly HashSet<string> Captures = new(StringComparer.OrdinalIgnoreCase)
    {
        CaptureNone,
        CaptureSummary,
        CaptureFull,
        CaptureOpaque
    };

    public ThinkingCapabilitiesDto GetCapabilities()
        => new(
            [ModeDisabled, ModeInterleaved, ModePreserved],
            [CaptureNone, CaptureSummary, CaptureFull, CaptureOpaque],
            GetDefaultPolicy());

    public ThinkingPolicyDto GetDefaultPolicy()
    {
        var thinking = options.Value.Thinking;
        return Normalize(new ThinkingPolicyDto
        {
            Enabled = thinking.Enabled,
            Mode = thinking.Mode,
            Capture = thinking.Capture,
            ExposeToClient = thinking.ExposeToClient,
            MaxPreservedTokens = thinking.MaxPreservedTokens,
            RawRequestOptionsJson = ParseRawOptions(thinking.RawRequestOptionsJson)
        });
    }

    public ThinkingPolicyDto Resolve(
        ThinkingPolicyDto? sessionPolicy,
        ThinkingPolicyDto? agentPolicy,
        ThinkingPolicyDto? modelPolicy)
    {
        var defaults = GetDefaultPolicy();
        return Normalize(new ThinkingPolicyDto
        {
            Enabled = sessionPolicy?.Enabled ?? agentPolicy?.Enabled ?? modelPolicy?.Enabled ?? defaults.Enabled,
            Mode = sessionPolicy?.Mode ?? agentPolicy?.Mode ?? modelPolicy?.Mode ?? defaults.Mode,
            Capture = sessionPolicy?.Capture ?? agentPolicy?.Capture ?? modelPolicy?.Capture ?? defaults.Capture,
            ExposeToClient = sessionPolicy?.ExposeToClient ?? agentPolicy?.ExposeToClient ?? modelPolicy?.ExposeToClient ?? defaults.ExposeToClient,
            MaxPreservedTokens = sessionPolicy?.MaxPreservedTokens ?? agentPolicy?.MaxPreservedTokens ?? modelPolicy?.MaxPreservedTokens ?? defaults.MaxPreservedTokens,
            RawRequestOptionsJson = CloneRawOptions(
                sessionPolicy?.RawRequestOptionsJson ??
                agentPolicy?.RawRequestOptionsJson ??
                modelPolicy?.RawRequestOptionsJson ??
                defaults.RawRequestOptionsJson)
        });
    }

    public static ThinkingPolicyDto ValidateOverride(ThinkingPolicyDto? policy)
        => policy is null ? new ThinkingPolicyDto() : Normalize(policy);

    private static ThinkingPolicyDto Normalize(ThinkingPolicyDto policy)
    {
        var enabled = policy.Enabled ?? false;
        var mode = string.IsNullOrWhiteSpace(policy.Mode) ? ModeDisabled : policy.Mode.Trim();
        var capture = string.IsNullOrWhiteSpace(policy.Capture) ? CaptureOpaque : policy.Capture.Trim();
        var maxTokens = policy.MaxPreservedTokens ?? 24000;

        if (!Modes.Contains(mode))
        {
            throw new AgentPlatformValidationException($"Unsupported thinking mode '{mode}'.");
        }

        if (!Captures.Contains(capture))
        {
            throw new AgentPlatformValidationException($"Unsupported thinking capture '{capture}'.");
        }

        if (string.Equals(mode, ModePreserved, StringComparison.OrdinalIgnoreCase) && !enabled)
        {
            throw new AgentPlatformValidationException("Preserved thinking requires enabled = true.");
        }

        if (maxTokens <= 0)
        {
            throw new AgentPlatformValidationException("MaxPreservedTokens must be positive.");
        }

        ValidateRawOptions(policy.RawRequestOptionsJson);

        return new ThinkingPolicyDto
        {
            Enabled = enabled,
            Mode = Canonicalize(mode, Modes),
            Capture = Canonicalize(capture, Captures),
            ExposeToClient = policy.ExposeToClient ?? false,
            MaxPreservedTokens = maxTokens,
            RawRequestOptionsJson = CloneRawOptions(policy.RawRequestOptionsJson)
        };
    }

    private static string Canonicalize(string value, IEnumerable<string> supported)
        => supported.First(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));

    private static JsonElement? ParseRawOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new AgentPlatformValidationException($"Thinking raw request options must be valid JSON: {ex.Message}");
        }
    }

    private static JsonElement? CloneRawOptions(JsonElement? rawOptions)
    {
        if (rawOptions is null)
        {
            return null;
        }

        ValidateRawOptions(rawOptions);
        return rawOptions.Value.Clone();
    }

    private static void ValidateRawOptions(JsonElement? rawOptions)
    {
        if (rawOptions is null)
        {
            return;
        }

        if (rawOptions.Value.ValueKind != JsonValueKind.Object)
        {
            throw new AgentPlatformValidationException("Thinking raw request options must be a JSON object.");
        }
    }
}
