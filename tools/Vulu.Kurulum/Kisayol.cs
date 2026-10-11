using System.Runtime.InteropServices;

namespace Vulu.Kurulum;

// Windows kısayolu (.lnk) oluşturur. Kısayol dosyalarını yazmanın desteklenen tek yolu kabuğun COM arayüzüdür.
internal static class Kisayol
{
    public static void Olustur(string kisayolYolu, string hedef, string aciklama)
    {
        var baglanti = (IShellLinkW)new ShellLink();
        try
        {
            baglanti.SetPath(hedef);
            baglanti.SetWorkingDirectory(Path.GetDirectoryName(hedef)!);
            baglanti.SetDescription(aciklama);
            baglanti.SetIconLocation(hedef, 0);

            Directory.CreateDirectory(Path.GetDirectoryName(kisayolYolu)!);
            ((IPersistFile)baglanti).Save(kisayolYolu, hatirla: true);
        }
        finally
        {
            Marshal.ReleaseComObject(baglanti);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    // Çalışma zamanındaki hazır IPersistFile tanımı kullanılmaz: kurulum dosyası küçültülürken o tanımın
    // çağrılmayan yöntemleri atılır, kalanların sırası kayar ve Save yerine başka bir yöntem çağrılır.
    [ComImport, Guid("0000010B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID();
        void IsDirty();
        void Load();
        void Save([MarshalAs(UnmanagedType.LPWStr)] string dosya, [MarshalAs(UnmanagedType.Bool)] bool hatirla);
    }

    // Yalnızca kullanılan yöntemler adlandırıldı; diğerleri arayüzdeki sıralarını korumak için durur.
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath();
        void GetIDList();
        void SetIDList();
        void GetDescription();
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string aciklama);
        void GetWorkingDirectory();
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string klasor);
        void GetArguments();
        void SetArguments();
        void GetHotkey();
        void SetHotkey();
        void GetShowCmd();
        void SetShowCmd();
        void GetIconLocation();
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string dosya, int sira);
        void SetRelativePath();
        void Resolve();
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string hedef);
    }
}
