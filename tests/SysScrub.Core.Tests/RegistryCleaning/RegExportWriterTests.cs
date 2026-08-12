using System.Diagnostics;
using System.Security;
using Microsoft.Win32;
using SysScrub.Core.RegistryCleaning;
using Xunit;

namespace SysScrub.Core.Tests.RegistryCleaning;

/// <summary>
/// Yedekleme testleri gerçek registry üzerinde çalışır — HKCU\Software\SysScrub.Tests
/// altındaki kendi kum havuzunda. Testin iddiası şu: yazdığımız .reg dosyası,
/// silinen her şeyi birebir geri getirmeli. Getirmiyorsa "geri alınabilir" sözü boş demektir.
/// </summary>
public sealed class RegExportWriterTests : IDisposable
{
    private const string SandboxRoot = @"SOFTWARE\SysScrub.Tests";

    private readonly string _keyPath;
    private readonly string _regFile;

    public RegExportWriterTests()
    {
        _keyPath = $@"{SandboxRoot}\{Guid.NewGuid():N}";
        _regFile = Path.Combine(Path.GetTempPath(), $"sysscrub-yedek-{Guid.NewGuid():N}.reg");
    }

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
        }

        try
        {
            File.Delete(_regFile);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void TumVeriTurleriYedeklenirVeGeriYuklenir()
    {
        var expected = new Dictionary<string, (object Value, RegistryValueKind Kind)>
        {
            ["Metin"] = ("düz metin", RegistryValueKind.String),
            ["TurkceKarakterler"] = ("ıöüşçğİÖÜŞÇĞ", RegistryValueKind.String),
            ["TirnakliMetin"] = ("içinde \"tırnak\" var", RegistryValueKind.String),
            ["TersEgikCizgi"] = (@"C:\Program Files\Uygulama\app.exe", RegistryValueKind.String),
            ["GenisletilenMetin"] = (@"%SystemRoot%\system32\notepad.exe", RegistryValueKind.ExpandString),
            ["CokluMetin"] = (new[] { "birinci", "ikinci", "üçüncü" }, RegistryValueKind.MultiString),
            ["Sayi"] = (42, RegistryValueKind.DWord),
            ["BuyukSayi"] = (9_000_000_000L, RegistryValueKind.QWord),
            ["Ikili"] = (new byte[] { 0x00, 0x01, 0xFE, 0xFF, 0x7F }, RegistryValueKind.Binary)
        };

        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(_keyPath))
        {
            key.SetValue(null, "varsayılan değer", RegistryValueKind.String);

            foreach ((string name, (object value, RegistryValueKind kind)) in expected)
            {
                key.SetValue(name, value, kind);
            }
        }

        var location = new RegistryLocation
        {
            Hive = RegistryHive.CurrentUser,
            KeyPath = _keyPath
        };

        RegExportWriter.Write(_regFile, [location]);

        Assert.True(File.Exists(_regFile));

        Registry.CurrentUser.DeleteSubKeyTree(_keyPath);
        Assert.Null(Registry.CurrentUser.OpenSubKey(_keyPath));

        Assert.True(ImportRegFile(_regFile), "reg import başarısız");

        using RegistryKey? restored = Registry.CurrentUser.OpenSubKey(_keyPath);
        Assert.NotNull(restored);

        Assert.Equal("varsayılan değer", restored!.GetValue(null));

        foreach ((string name, (object value, RegistryValueKind kind)) in expected)
        {
            Assert.Equal(kind, restored.GetValueKind(name));

            object? actual = restored.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

            if (value is byte[] bytes)
            {
                Assert.Equal(bytes, (byte[])actual!);
            }
            else if (value is string[] strings)
            {
                Assert.Equal(strings, (string[])actual!);
            }
            else
            {
                Assert.Equal(value.ToString(), actual?.ToString());
            }
        }
    }

    [Fact]
    public void TekDegerYedeklendigindeKomsuDegerlerDosyayaGirmez()
    {
        // Bulgularımızın çoğu büyük bir anahtarın içindeki tek değer. Tüm anahtarı
        // yedeklemek, geri yüklerken kullanıcının aradan yaptığı değişiklikleri de geri sarardı.
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(_keyPath))
        {
            key.SetValue("Silinecek", @"C:\yok\uygulama.exe");
            key.SetValue("Dokunulmayacak", "bu yedekte olmamalı");
        }

        RegExportWriter.Write(_regFile, [new RegistryLocation
        {
            Hive = RegistryHive.CurrentUser,
            KeyPath = _keyPath,
            ValueName = "Silinecek"
        }]);

        string content = File.ReadAllText(_regFile);

        Assert.Contains("Silinecek", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Dokunulmayacak", content, StringComparison.Ordinal);
    }

    [Fact]
    public void AltAnahtarlarDaYedeklenir()
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey($@"{_keyPath}\Alt\Daha Alt"))
        {
            key.SetValue("Derin", "derindeki değer");
        }

        RegExportWriter.Write(_regFile, [new RegistryLocation
        {
            Hive = RegistryHive.CurrentUser,
            KeyPath = _keyPath
        }]);

        Registry.CurrentUser.DeleteSubKeyTree(_keyPath);
        Assert.True(ImportRegFile(_regFile));

        using RegistryKey? restored = Registry.CurrentUser.OpenSubKey($@"{_keyPath}\Alt\Daha Alt");

        Assert.NotNull(restored);
        Assert.Equal("derindeki değer", restored!.GetValue("Derin"));
    }

    [Fact]
    public void AyniAnahtardakiCokluDegerTekBloktaToplanir()
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(_keyPath))
        {
            key.SetValue("Bir", "1");
            key.SetValue("Iki", "2");
        }

        RegExportWriter.Write(_regFile,
        [
            new RegistryLocation { Hive = RegistryHive.CurrentUser, KeyPath = _keyPath, ValueName = "Bir" },
            new RegistryLocation { Hive = RegistryHive.CurrentUser, KeyPath = _keyPath, ValueName = "Iki" }
        ]);

        string content = File.ReadAllText(_regFile);
        int blockCount = content.Split("[HKEY_CURRENT_USER").Length - 1;

        Assert.Equal(1, blockCount);
        Assert.Contains("\"Bir\"", content, StringComparison.Ordinal);
        Assert.Contains("\"Iki\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void OkunamayanKonumSessizceAtlanir()
    {
        // Var olmayan anahtar dosyayı bozmamalı, yalnızca o blok yazılmamalı.
        RegExportWriter.Write(_regFile, [new RegistryLocation
        {
            Hive = RegistryHive.CurrentUser,
            KeyPath = $@"{SandboxRoot}\kesinlikle-yok-{Guid.NewGuid():N}"
        }]);

        Assert.True(File.Exists(_regFile));
        Assert.StartsWith("Windows Registry Editor Version 5.00", File.ReadAllText(_regFile), StringComparison.Ordinal);
    }

    private static bool ImportRegFile(string path)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "reg.exe",
            Arguments = $"import \"{path}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (process is null)
        {
            return false;
        }

        process.WaitForExit(30_000);
        return process.ExitCode == 0;
    }
}
