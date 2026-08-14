using System.Security.Cryptography;

namespace SysScrub.Core.Updates;

/// <summary>
/// Yayına eklenen SHA256SUMS.txt dosyası.
///
/// Biçim, sha256sum aracının çıktısıyla aynı: her satırda özet, iki boşluk ve
/// dosya adı. Doğrulama yapılamıyorsa kurulum başlatılmıyor — indirme bozuksa
/// ya da araya biri girdiyse kullanıcının bunu bilmemesi kabul edilemez.
/// </summary>
public sealed class ChecksumList
{
    private readonly Dictionary<string, string> _hashes;

    private ChecksumList(Dictionary<string, string> hashes) => _hashes = hashes;

    public int Count => _hashes.Count;

    public static ChecksumList Parse(string content)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // PowerShell'in "-Encoding utf8" çıktısı BOM ile başlıyor ve .NET onu
        // boşluk saymıyor; kırpmadan ilk satırın özeti 65 karakter görünüp
        // atlanırdı. Yayını üreten iş akışı da aynı biçimi yazıyor.
        content = content.TrimStart('﻿');

        foreach (string line in content.Split('\n'))
        {
            ReadOnlySpan<char> trimmed = line.AsSpan().Trim();

            if (trimmed.IsEmpty || trimmed[0] == '#')
            {
                continue;
            }

            int separator = trimmed.IndexOfAny(' ', '\t');

            if (separator <= 0)
            {
                continue;
            }

            ReadOnlySpan<char> hash = trimmed[..separator];

            // sha256sum ikili kip için dosya adının başına "*" koyar.
            ReadOnlySpan<char> name = trimmed[separator..].TrimStart(" \t*".AsSpan());

            if (hash.Length != 64 || name.IsEmpty)
            {
                continue;
            }

            hashes[name.ToString()] = hash.ToString();
        }

        return new ChecksumList(hashes);
    }

    /// <summary>Dosya listede yoksa null döner — "eşleşmedi" ile karıştırılmasın.</summary>
    public string? Find(string fileName) =>
        _hashes.TryGetValue(fileName, out string? hash) ? hash : null;

    public static async Task<string> ComputeAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);

        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

        return Convert.ToHexString(hash);
    }
}
