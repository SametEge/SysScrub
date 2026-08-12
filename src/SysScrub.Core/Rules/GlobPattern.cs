using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace SysScrub.Core.Rules;

/// <summary>
/// Kural dosyalarındaki include/exclude desenlerini eşleştirir.
///
/// Hazır bir glob kütüphanesi yerine kendi eşleştiricimiz var çünkü mevcut olanlar
/// dizini kendileri dolaşmak istiyor; bizim tarayıcımızın bağlantı noktalarını atlaması
/// ve erişim hatalarını yutması gerekiyor, dolaşmanın kontrolü bizde kalmalı.
///
/// Desteklenen sözdizimi:
///   *   ayırıcı hariç herhangi bir dizi   ("*.log")
///   **  ayırıcı dahil herhangi bir dizi   ("**/*.tmp")
///   ?   tek karakter
/// </summary>
public sealed class GlobPattern
{
    private static readonly ConcurrentDictionary<string, GlobPattern> Cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly Regex _regex;

    private GlobPattern(string pattern, Regex regex)
    {
        Pattern = pattern;
        _regex = regex;
    }

    public string Pattern { get; }

    /// <summary>Her şeyi eşleyen desen; kurallar include belirtmediğinde kullanılır.</summary>
    public static GlobPattern MatchAll { get; } = Parse("**/*");

    public static GlobPattern Parse(string pattern) =>
        Cache.GetOrAdd(pattern, static p => new GlobPattern(p, Compile(p)));

    /// <summary>Kök klasöre göreli yolu eşleştirir. Yol ayırıcısı fark etmez.</summary>
    public bool IsMatch(string relativePath) => _regex.IsMatch(Normalize(relativePath));

    public static bool IsMatchAny(IReadOnlyList<GlobPattern> patterns, string relativePath)
    {
        for (int i = 0; i < patterns.Count; i++)
        {
            if (patterns[i].IsMatch(relativePath))
            {
                return true;
            }
        }

        return false;
    }

    public override string ToString() => Pattern;

    private static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');

    private static Regex Compile(string pattern)
    {
        string normalized = Normalize(pattern);
        var builder = new StringBuilder("^");

        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];

            switch (c)
            {
                case '*' when i + 1 < normalized.Length && normalized[i + 1] == '*':
                    // "**/" biçimi sıfır klasör de eşlemeli: "**/*.log" deseni kökteki
                    // "a.log" dosyasını da yakalamalı, yoksa kurallar sezgiye aykırı davranır.
                    if (i + 2 < normalized.Length && normalized[i + 2] == '/')
                    {
                        builder.Append("(?:.*/)?");
                        i += 2;
                    }
                    else
                    {
                        builder.Append(".*");
                        i++;
                    }

                    break;

                case '*':
                    builder.Append("[^/]*");
                    break;

                case '?':
                    builder.Append("[^/]");
                    break;

                case '/':
                    builder.Append('/');
                    break;

                default:
                    builder.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        builder.Append('$');

        return new Regex(
            builder.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }
}
