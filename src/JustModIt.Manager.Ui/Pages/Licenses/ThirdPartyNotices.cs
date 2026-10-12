using System.Text;
using System.Text.RegularExpressions;

namespace JustModIt.Manager.Ui.Pages.Licenses;

/// <summary>A component of others that ships with the app, as the third-party notices describe it.</summary>
/// <param name="Name">The component's name.</param>
/// <param name="Version">Its versions, as the notices give them; empty when they give none.</param>
/// <param name="License">Its licence in a few words; null when several licences apply.</param>
/// <param name="Notice">Its part of the notices, with the licence texts and upstream notices that part refers to, in Markdown.</param>
public sealed record ThirdPartyComponent(string Name, string Version, string? License, string Notice);

/// <summary>
/// Reads the components out of the app's <c>THIRD-PARTY-NOTICES.md</c>: each one under "Components", with the texts
/// under "Licence texts" and "Upstream notices" that it names in quotes.
/// </summary>
internal static partial class ThirdPartyNotices
{
    private const string Components = "Components";

    /// <summary>The components of the notices, in their order.</summary>
    /// <param name="markdown">The whole notices file.</param>
    public static IReadOnlyList<ThirdPartyComponent> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var sections = Sections(markdown);
        var texts = sections.Where(section => section.Part != Components)
            .ToDictionary(section => section.Title, section => $"### {section.Title}\n\n{section.Body}", StringComparer.Ordinal);
        return
        [
            .. sections.Where(section => section.Part == Components).Select(section =>
            {
                var fields = Fields(section.Body);
                var referred = Quoted().Matches(section.Body).Select(match => match.Groups[1].Value).Where(texts.ContainsKey).Distinct();
                var notice = string.Join("\n\n", [section.Body, .. referred.Select(name => texts[name])]);
                return new ThirdPartyComponent(section.Title, Version(fields), fields.TryGetValue("Licence", out var license) ? Short(license) : null,
                    notice);
            }),
        ];
    }

    // Lines inside code fences are the licences' own text, whose headings are not the file's.
    private static List<(string Part, string Title, string Body)> Sections(string markdown)
    {
        var sections = new List<(string Part, string Title, string Body)>();
        var part = "";
        string? title = null;
        var body = new StringBuilder();
        var fenced = false;

        void Close()
        {
            if (title is not null)
            {
                sections.Add((part, title, body.ToString().Trim()));
            }

            title = null;
            body.Clear();
        }

        foreach (var line in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
            }
            else if (!fenced && line.StartsWith("## ", StringComparison.Ordinal))
            {
                Close();
                part = line[3..].Trim();
                continue;
            }
            else if (!fenced && line.StartsWith("### ", StringComparison.Ordinal))
            {
                Close();
                title = line[4..].Trim();
                continue;
            }

            body.Append(line).Append('\n');
        }

        Close();
        return sections;
    }

    // "- Key: value" lines, a value going on over the indented lines under it.
    private static Dictionary<string, string> Fields(string body)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        string? key = null;
        foreach (var line in body.Split('\n'))
        {
            if (Field().Match(line) is { Success: true } field)
            {
                key = field.Groups[1].Value;
                fields[key] = field.Groups[2].Value.Trim();
            }
            else if (key is not null && line.StartsWith("  ", StringComparison.Ordinal))
            {
                fields[key] += " " + line.Trim();
            }
            else
            {
                key = null;
            }
        }

        return fields;
    }

    private static string Version(Dictionary<string, string> fields)
    {
        if (fields.TryGetValue("Version", out var version))
        {
            return Short(version);
        }

        var packages = fields.GetValueOrDefault("NuGet packages") ?? fields.GetValueOrDefault("NuGet package") ?? "";
        return string.Join(" · ", PackageVersion().Matches(packages).Select(match => match.Groups[1].Value).Distinct());
    }

    // What comes before an explanation in brackets or after a comma or a semicolon.
    private static string Short(string value)
    {
        var end = value.IndexOfAny(['(', ',', ';']);
        return Commit().Replace((end < 0 ? value : value[..end]).Trim(), "$1");
    }

    // A whole commit hash is noise in a table; its first seven digits name it.
    [GeneratedRegex(@"\b([0-9a-f]{7})[0-9a-f]{33}\b")]
    private static partial Regex Commit();

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"^- ([A-Za-z][A-Za-z ]*): (.*)$")]
    private static partial Regex Field();

    [GeneratedRegex(@"\S+ (\d+(?:\.\d+)+)")]
    private static partial Regex PackageVersion();
}
