using System;
using System.Collections.Generic;
using System.Linq;

namespace Federated.AuthService.Services;

/// <summary>
/// Matches request paths against registered route patterns.
///
/// Pattern syntax (case-insensitive, query strings and trailing slashes are ignored):
///   /orders            literal segments
///   /orders/{id}       {name} (optionally {name:int}) matches exactly one segment
///   /orders/*          * matches exactly one segment
///   /reports/**        ** (last segment only) matches zero or more remaining segments
/// </summary>
public static class RoutePatternMatcher
{
    public static readonly string[] AllowedMethods =
        { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "*" };

    private const int LiteralScore = 100;
    private const int ParameterScore = 10;
    private const int SingleWildcardScore = 5;
    private const int CatchAllScore = 1;

    public static string NormalizePath(string? path)
    {
        string p = (path ?? string.Empty).Trim();

        int cut = p.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) p = p[..cut];

        if (!p.StartsWith('/')) p = "/" + p;
        if (p.Length > 1) p = p.TrimEnd('/');
        if (p.Length == 0) p = "/";

        return p;
    }

    public static string NormalizeMethod(string? method)
        => (method ?? string.Empty).Trim().ToUpperInvariant();

    public static bool IsValidMethod(string? method)
        => AllowedMethods.Contains(NormalizeMethod(method), StringComparer.Ordinal);

    /// <summary>Returns null when the pattern is valid, otherwise a human readable error.</summary>
    public static string? Validate(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return "Path pattern is required.";

        string p = pattern.Trim();
        if (!p.StartsWith('/')) return "Path pattern must start with '/'.";
        if (p.Any(char.IsWhiteSpace)) return "Path pattern must not contain whitespace.";
        if (p.Contains('?') || p.Contains('#')) return "Path pattern must not contain a query string or fragment.";

        var segments = Split(p);
        for (int i = 0; i < segments.Length; i++)
        {
            string s = segments[i];
            if (s == "**")
            {
                if (i != segments.Length - 1) return "'**' is only allowed as the last segment.";
                continue;
            }

            bool hasOpen = s.Contains('{');
            bool hasClose = s.Contains('}');
            if (hasOpen || hasClose)
            {
                if (!(s.StartsWith('{') && s.EndsWith('}') && s.Count(c => c == '{') == 1 && s.Count(c => c == '}') == 1 && s.Length > 2))
                {
                    return $"Invalid parameter segment '{s}'. Use the form '{{name}}'.";
                }
            }
            else if (s.Contains('*') && s != "*")
            {
                return $"Invalid wildcard segment '{s}'. Use '*' or '**' as a whole segment.";
            }
        }

        return null;
    }

    /// <summary>
    /// Tries to match a request path against a pattern. When matched, <paramref name="specificity"/>
    /// is higher for more specific patterns (literals beat parameters beat wildcards).
    /// </summary>
    public static bool TryMatch(string pattern, string path, out int specificity)
    {
        specificity = 0;

        var pSegs = Split(NormalizePath(pattern));
        var rSegs = Split(NormalizePath(path));

        int score = 0;
        for (int i = 0; i < pSegs.Length; i++)
        {
            string ps = pSegs[i];

            if (ps == "**")
            {
                score += CatchAllScore;
                specificity = score;
                return true; // swallows the rest (including nothing)
            }

            if (i >= rSegs.Length) return false;

            if (ps == "*")
            {
                score += SingleWildcardScore;
            }
            else if (ps.Length > 2 && ps.StartsWith('{') && ps.EndsWith('}'))
            {
                score += ParameterScore;
            }
            else if (string.Equals(ps, rSegs[i], StringComparison.OrdinalIgnoreCase))
            {
                score += LiteralScore;
            }
            else
            {
                return false;
            }
        }

        if (pSegs.Length != rSegs.Length) return false;

        specificity = score;
        return true;
    }

    private static string[] Split(string path)
        => path.Split('/', StringSplitOptions.RemoveEmptyEntries);
}
