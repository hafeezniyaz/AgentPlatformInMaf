using System.Globalization;
using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;
using AgentPlatform.Core.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Sqlite.Runtime;

public sealed class FileAgentSkillCatalog(
    IOptions<AgentPlatformOptions> options,
    ILogger<FileAgentSkillCatalog> logger) : IAgentSkillCatalog
{
    public async Task<IReadOnlyList<CatalogItemDto>> ListSkillsAsync(CancellationToken cancellationToken)
    {
        var skillsPath = Path.GetFullPath(options.Value.SkillsPath);
        if (!Directory.Exists(skillsPath))
        {
            return [];
        }

        var skills = new List<CatalogItemDto>();
        foreach (var skillDirectory in Directory.EnumerateDirectories(skillsPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var skillFile = Path.Combine(skillDirectory, "SKILL.md");
            if (!File.Exists(skillFile))
            {
                continue;
            }

            var skill = await TryReadSkillAsync(skillsPath, skillFile, cancellationToken);
            if (skill is not null)
            {
                skills.Add(skill);
            }
        }

        return skills
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<CatalogItemDto?> TryReadSkillAsync(
        string skillsPath,
        string skillFile,
        CancellationToken cancellationToken)
    {
        try
        {
            var parentDirectory = Path.GetDirectoryName(skillFile);
            if (string.IsNullOrWhiteSpace(parentDirectory) ||
                string.Equals(parentDirectory, skillsPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var text = await File.ReadAllTextAsync(skillFile, cancellationToken);
            var frontmatter = ParseFrontmatter(text);
            if (!frontmatter.TryGetValue("name", out var name) ||
                !frontmatter.TryGetValue("description", out var description))
            {
                return null;
            }

            var directoryName = Path.GetFileName(parentDirectory);
            if (!string.Equals(directoryName, name, StringComparison.Ordinal))
            {
                return null;
            }

#pragma warning disable MAAI001
            var validated = new AgentSkillFrontmatter(name, description)
            {
                License = frontmatter.GetValueOrDefault("license"),
                Compatibility = frontmatter.GetValueOrDefault("compatibility"),
                AllowedTools = frontmatter.GetValueOrDefault("allowed-tools")
            };
#pragma warning restore MAAI001

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddMetadata(metadata, "license", validated.License);
            AddMetadata(metadata, "compatibility", validated.Compatibility);
            AddMetadata(metadata, "allowedTools", validated.AllowedTools);

            foreach (var item in frontmatter.Where(item => item.Key.StartsWith("metadata.", StringComparison.Ordinal)))
            {
                AddMetadata(metadata, item.Key, item.Value);
            }

            return new CatalogItemDto(
                validated.Name,
                ToDisplayName(validated.Name),
                validated.Description,
                "skill",
                metadata.Count == 0 ? null : metadata);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
        {
            logger.LogWarning(ex, "Skipping invalid skill file '{SkillFile}'.", skillFile);
            return null;
        }
    }

    private static Dictionary<string, string> ParseFrontmatter(string text)
    {
        using var reader = new StringReader(text);
        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
        {
            throw new FormatException("SKILL.md must start with YAML frontmatter.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var inMetadata = false;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                return values;
            }

            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("  ", StringComparison.Ordinal))
            {
                if (inMetadata)
                {
                    ParseKeyValue(line.Trim(), values, "metadata.");
                }

                continue;
            }

            inMetadata = line.StartsWith("metadata:", StringComparison.Ordinal);
            if (!inMetadata)
            {
                ParseKeyValue(line.Trim(), values, "");
            }
        }

        throw new FormatException("SKILL.md frontmatter is not closed.");
    }

    private static void ParseKeyValue(string line, Dictionary<string, string> values, string prefix)
    {
        var separator = line.IndexOf(':');
        if (separator <= 0)
        {
            return;
        }

        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim().Trim('"', '\'');
        if (key.Length > 0 && value.Length > 0)
        {
            values[prefix + key] = value;
        }
    }

    private static void AddMetadata(Dictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }

    private static string ToDisplayName(string name)
        => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('-', ' '));
}
