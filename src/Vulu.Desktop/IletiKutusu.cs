using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Vulu.Desktop;

// Uygulamanın paletinde çizilen ileti penceresi: bilgi, hata, Evet/Hayır sorusu ve uzun süren bir iş sırasında
// gösterilen bekleme penceresi. Windows'un hazır ileti kutusu renklendirilemediği için pencere doğrudan Windows
// arayüzüyle (user32 / gdi32) çizilir; bunun dışında bir bileşene bağlı değildir, Windows 10 ve 11'de aynı çalışır.
// Düğmeler gerçek Windows düğmeleridir (yalnızca görünümleri bizden): klavye ve ekran okuyucu ile kullanılabilir.
//
// Kurulum programı da bu dosyayı kullanır (tools/Vulu.Kurulum); arayüz açılmadan önceki iletiler de buradadır.
[SupportedOSPlatform("windows")]
internal static class IletiKutusu
{
    public enum Tur { Bilgi, Hata, Soru }

    // Renkler wwwroot/css/app.css ile aynıdır.
    private static readonly int Zemin = Renk(0x2b, 0x1a, 0x10);
    private static readonly int Metin = Renk(0xf3, 0xe6, 0xd6);
    private static readonly int Kenar = Renk(0x4a, 0x2e, 0x1c);
    private static readonly int Vurgu = Renk(0xb5, 0x80, 0x4f);
    private static readonly int VurguBasili = Renk(0x9a, 0x6a, 0x3e);
    private static readonly int VurguMetni = Renk(0x1a, 0x11, 0x0a);
    private static readonly int YuzeyAcik = Renk(0x3a, 0x24, 0x16);
    private static readonly int OdakKenari = Renk(0xe9, 0xc6, 0x99);
    private static readonly int HataRengi = Renk(0xe5, 0x60, 0x4d);

    // Ölçüler %100 ekran ölçeğine göredir; pencere açılırken ekranın ölçeğiyle çarpılır.
    private const int Genislik = 460, Bosluk = 24, IkonBoyutu = 40, IkonAraligi = 16, SeritKalinligi = 4;
    private const int DugmeGenisligi = 108, DugmeYuksekligi = 34, DugmeAraligi = 10, DugmeBolmesi = 74;
    private const int YaziBoyutu = 15, EnAzMetinYuksekligi = IkonBoyutu;

    private const int BirincilDugme = 1, IkincilDugme = 2;   // IDOK ve IDCANCEL: Enter ve Esc bunlara gider.
    private const string SinifAdi = "VuluIletiKutusu";

    private static readonly WndProc Islev = PencereIslevi;
    private static bool _sinifKayitli;

    // Aynı anda tek pencere açıktır; durumu burada tutulur.
    private static string _mesaj = "";
    private static Tur _tur;
    private static bool _sonuc, _bitti, _dugmeli;
    private static double _olcek = 1;
    private static IntPtr _yazi, _kalinYazi, _ikon;

    // Bilgi ve hata iletilerinde tek düğme vardır ve her zaman true döner; soruda kullanıcı Evet derse true döner.
    public static bool Goster(string mesaj, Tur tur) => Ac(mesaj, tur, dugmeli: true, pencere => { });

    // İş sürerken düğmesiz bir bekleme penceresi gösterir; iş bitince pencere kendiliğinden kapanır. İşin fırlattığı
    // hata çağırana aynen iletilir.
    public static void Calistir(string mesaj, Action isParcasi)
    {
        ExceptionDispatchInfo? hata = null;

        Ac(mesaj, Tur.Bilgi, dugmeli: false, pencere => Task.Run(() =>
        {
            try { isParcasi(); }
            catch (Exception yakalanan) { hata = ExceptionDispatchInfo.Capture(yakalanan); }
            finally { PostMessageW(pencere, IsBitti, IntPtr.Zero, IntPtr.Zero); }
        }));

        hata?.Throw();
    }

    private static bool Ac(string mesaj, Tur tur, bool dugmeli, Action<IntPtr> acilinca)
    {
        NetCizimiAc();
        SinifiKaydet();

        _mesaj = mesaj;
        _tur = tur;
        _sonuc = tur != Tur.Soru;
        _bitti = false;
        _dugmeli = dugmeli;

        // Kapatma düğmesi yalnızca kullanıcının yanıt verebildiği pencerelerde bulunur.
        var stil = WS_POPUP | WS_CAPTION | (dugmeli ? WS_SYSMENU : 0);
        var pencere = CreateWindowExW(0, SinifAdi, "vulu", stil, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        if (pencere == IntPtr.Zero) throw new InvalidOperationException("İleti penceresi oluşturulamadı.");

        _olcek = EkranOlcegi(pencere);
        _yazi = YaziOlustur(kalin: false);
        _kalinYazi = YaziOlustur(kalin: true);
        _ikon = LoadImageW(GetModuleHandleW(null), UygulamaSimgesi, IMAGE_ICON, Olcekle(IkonBoyutu), Olcekle(IkonBoyutu), 0);

        try
        {
            var metinYuksekligi = Math.Max(MetinYuksekligi(pencere), Olcekle(EnAzMetinYuksekligi));
            var icGenislik = Olcekle(Genislik);
            var icYukseklik = Olcekle(SeritKalinligi + Bosluk) + metinYuksekligi + Olcekle(Bosluk) + (dugmeli ? Olcekle(DugmeBolmesi) : 0);

            if (dugmeli) DugmeleriOlustur(pencere, icGenislik, icYukseklik);
            Yerlestir(pencere, stil, icGenislik, icYukseklik);
            WindowsUyumlulugu.BaslikCubugunuBoya(pencere);

            ShowWindow(pencere, SW_SHOW);
            SetForegroundWindow(pencere);
            if (dugmeli) SetFocus(GetDlgItem(pencere, BirincilDugme));
            acilinca(pencere);

            // IsDialogMessage Sekme, Enter ve Esc tuşlarını düğmelere yönlendirir.
            while (!_bitti && GetMessageW(out var ileti, IntPtr.Zero, 0, 0) > 0)
            {
                if (IsDialogMessageW(pencere, ref ileti)) continue;
                TranslateMessage(ref ileti);
                DispatchMessageW(ref ileti);
            }

            return _sonuc;
        }
        finally
        {
            if (!_bitti) DestroyWindow(pencere);
            DeleteObject(_yazi);
            DeleteObject(_kalinYazi);
            if (_ikon != IntPtr.Zero) DestroyIcon(_ikon);
        }
    }

    private static void DugmeleriOlustur(IntPtr pencere, int icGenislik, int icYukseklik)
    {
        var y = icYukseklik - Olcekle(DugmeBolmesi) + Olcekle((DugmeBolmesi - DugmeYuksekligi) / 2);
        var x = icGenislik - Olcekle(Bosluk) - Olcekle(DugmeGenisligi);

        if (_tur == Tur.Soru)
        {
            DugmeOlustur(pencere, "Hayır", IkincilDugme, x, y);
            x -= Olcekle(DugmeGenisligi + DugmeAraligi);
        }

        DugmeOlustur(pencere, _tur == Tur.Soru ? "Evet" : "Tamam", BirincilDugme, x, y);
    }

    private static void DugmeOlustur(IntPtr pencere, string metin, int kimlik, int x, int y)
    {
        var dugme = CreateWindowExW(0, "BUTTON", metin, WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW,
            x, y, Olcekle(DugmeGenisligi), Olcekle(DugmeYuksekligi), pencere, kimlik, GetModuleHandleW(null), IntPtr.Zero);
        SendMessageW(dugme, WM_SETFONT, _kalinYazi, 1);
    }

    // İç alanı istenen ölçüde olacak şekilde pencereyi boyutlandırır ve ekranın ortasına koyar.
    private static void Yerlestir(IntPtr pencere, int stil, int icGenislik, int icYukseklik)
    {
        var cerceve = new RECT { Right = icGenislik, Bottom = icYukseklik };
        AdjustWindowRectEx(ref cerceve, stil, false, 0);

        int genislik = cerceve.Right - cerceve.Left, yukseklik = cerceve.Bottom - cerceve.Top;
        var x = (GetSystemMetrics(SM_CXSCREEN) - genislik) / 2;
        var y = (GetSystemMetrics(SM_CYSCREEN) - yukseklik) / 2;
        SetWindowPos(pencere, IntPtr.Zero, x, y, genislik, yukseklik, SWP_NOZORDER);
    }

    private static IntPtr PencereIslevi(IntPtr pencere, uint ileti, IntPtr w, IntPtr l)
    {
        switch (ileti)
        {
            case WM_ERASEBKGND:
                return 1;   // Zemin WM_PAINT içinde boyanır; ayrıca silmek titremeye yol açar.

            case WM_PAINT:
                Boya(pencere);
                return IntPtr.Zero;

            case WM_DRAWITEM:
                DugmeyiCiz(Marshal.PtrToStructure<DRAWITEMSTRUCT>(l));
                return 1;

            case DM_GETDEFID:
                // Enter, odak hangi düğmede olursa olsun birincil düğmeye basar.
                return (DC_HASDEFID << 16) | BirincilDugme;

            case WM_COMMAND:
                var kimlik = (int)(w.ToInt64() & 0xFFFF);
                if (kimlik is BirincilDugme or IkincilDugme)
                {
                    // Tek düğmeli pencerede Esc de onay sayılır; soruda Hayır demektir.
                    _sonuc = kimlik == BirincilDugme || _tur != Tur.Soru;
                    DestroyWindow(pencere);
                }
                return IntPtr.Zero;

            case WM_CLOSE:
                // Bekleme penceresi iş bitmeden kapatılamaz (Alt+F4 dahil); iş bitince IsBitti iletisiyle kapanır.
                if (!_dugmeli) return IntPtr.Zero;
                if (_tur == Tur.Soru) _sonuc = false;
                DestroyWindow(pencere);
                return IntPtr.Zero;

            case IsBitti:
                DestroyWindow(pencere);
                return IntPtr.Zero;

            case WM_DESTROY:
                _bitti = true;
                // Bekleyen GetMessage'ın dönmesi için kuyruğa boş bir ileti bırakılır.
                PostMessageW(IntPtr.Zero, WM_NULL, IntPtr.Zero, IntPtr.Zero);
                return IntPtr.Zero;
        }

        return DefWindowProcW(pencere, ileti, w, l);
    }

    private static void Boya(IntPtr pencere)
    {
        var dc = BeginPaint(pencere, out var boyama);
        GetClientRect(pencere, out var alan);

        Doldur(dc, alan, Zemin);
        Doldur(dc, alan with { Bottom = Olcekle(SeritKalinligi) }, _tur == Tur.Hata ? HataRengi : Vurgu);

        var ust = Olcekle(SeritKalinligi + Bosluk);
        if (_ikon != IntPtr.Zero)
            DrawIconEx(dc, Olcekle(Bosluk), ust, _ikon, Olcekle(IkonBoyutu), Olcekle(IkonBoyutu), 0, IntPtr.Zero, DI_NORMAL);

        var metinAlani = MetinAlani(alan.Right, ust);
        var eskiYazi = SelectObject(dc, _yazi);
        _ = SetBkMode(dc, TRANSPARENT);
        _ = SetTextColor(dc, Metin);
        _ = DrawTextW(dc, _mesaj, -1, ref metinAlani, DT_WORDBREAK | DT_NOPREFIX);
        SelectObject(dc, eskiYazi);

        // Düğme bölmesini içerikten ayıran çizgi.
        if (GetDlgItem(pencere, BirincilDugme) != IntPtr.Zero)
        {
            var cizgi = alan.Bottom - Olcekle(DugmeBolmesi);
            Doldur(dc, alan with { Top = cizgi, Bottom = cizgi + 1 }, Kenar);
        }

        EndPaint(pencere, ref boyama);
    }

    private static void DugmeyiCiz(DRAWITEMSTRUCT cizim)
    {
        var birincil = cizim.CtlID == BirincilDugme;
        var basili = (cizim.itemState & ODS_SELECTED) != 0;
        var odakta = (cizim.itemState & ODS_FOCUS) != 0;

        var dolgu = birincil ? (basili ? VurguBasili : Vurgu) : (basili ? YuzeyAcik : Zemin);
        var kenar = odakta ? OdakKenari : birincil ? dolgu : Kenar;

        // Köşelerin dışında kalan kısım pencere zeminiyle aynı olmalı.
        Doldur(cizim.hDC, cizim.rcItem, Zemin);

        var firca = CreateSolidBrush(dolgu);
        var kalem = CreatePen(PS_SOLID, odakta ? Olcekle(2) : 1, kenar);
        var eskiFirca = SelectObject(cizim.hDC, firca);
        var eskiKalem = SelectObject(cizim.hDC, kalem);
        var kose = Olcekle(6);
        RoundRect(cizim.hDC, cizim.rcItem.Left, cizim.rcItem.Top, cizim.rcItem.Right, cizim.rcItem.Bottom, kose, kose);
        SelectObject(cizim.hDC, eskiFirca);
        SelectObject(cizim.hDC, eskiKalem);
        DeleteObject(firca);
        DeleteObject(kalem);

        var metin = new char[64];
        var uzunluk = GetWindowTextW(cizim.hwndItem, metin, metin.Length);
        var alan = cizim.rcItem;
        _ = SetBkMode(cizim.hDC, TRANSPARENT);
        _ = SetTextColor(cizim.hDC, birincil ? VurguMetni : Metin);
        _ = DrawTextW(cizim.hDC, new string(metin, 0, uzunluk), -1, ref alan, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
    }

    // Metnin, simgenin sağındaki sütuna sığdırıldığında kaplayacağı yükseklik.
    private static int MetinYuksekligi(IntPtr pencere)
    {
        var dc = GetDC(pencere);
        var eskiYazi = SelectObject(dc, _yazi);
        var alan = MetinAlani(Olcekle(Genislik), 0);
        _ = DrawTextW(dc, _mesaj, -1, ref alan, DT_WORDBREAK | DT_NOPREFIX | DT_CALCRECT);
        SelectObject(dc, eskiYazi);
        _ = ReleaseDC(pencere, dc);
        return alan.Bottom - alan.Top;
    }

    private static RECT MetinAlani(int icGenislik, int ust)
    {
        var sol = Olcekle(Bosluk) + (_ikon != IntPtr.Zero ? Olcekle(IkonBoyutu + IkonAraligi) : 0);
        return new RECT { Left = sol, Top = ust, Right = icGenislik - Olcekle(Bosluk), Bottom = ust + 10_000 };
    }

    private static void Doldur(IntPtr dc, RECT alan, int renk)
    {
        var firca = CreateSolidBrush(renk);
        _ = FillRect(dc, ref alan, firca);
        DeleteObject(firca);
    }

    private static IntPtr YaziOlustur(bool kalin) =>
        CreateFontW(-Olcekle(YaziBoyutu), 0, 0, 0, kalin ? 600 : 400, 0, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, "Segoe UI");

    private static int Olcekle(int deger) => (int)Math.Round(deger * _olcek);

    // Windows renkleri 0x00BBGGRR sırasıyla bekler.
    private static int Renk(int kirmizi, int yesil, int mavi) => kirmizi | (yesil << 8) | (mavi << 16);

    private static void SinifiKaydet()
    {
        if (_sinifKayitli) return;

        var modul = GetModuleHandleW(null);
        var sinif = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Islev),
            hInstance = modul,
            hIcon = LoadImageW(modul, UygulamaSimgesi, IMAGE_ICON, 0, 0, LR_DEFAULTSIZE),
            hCursor = LoadCursorW(IntPtr.Zero, IDC_ARROW),
            lpszClassName = SinifAdi
        };
        if (RegisterClassExW(ref sinif) == 0) throw new InvalidOperationException("İleti penceresi sınıfı kaydedilemedi.");
        _sinifKayitli = true;
    }

    // Yüksek çözünürlüklü ekranlarda pencere bulanık büyütülmek yerine ekranın ölçeğinde çizilir. Çağrı, süreç
    // için bu ayar daha önce yapılmışsa ya da Windows sürümü desteklemiyorsa etkisizdir.
    private static void NetCizimiAc()
    {
        try { _ = SetProcessDpiAwarenessContext(EkranBasinaOlcek); }
        catch (EntryPointNotFoundException) { }
    }

    private static double EkranOlcegi(IntPtr pencere)
    {
        const double StandartDpi = 96;
        try
        {
            var dpi = GetDpiForWindow(pencere);
            return dpi == 0 ? 1 : dpi / StandartDpi;
        }
        catch (EntryPointNotFoundException)
        {
            return 1;
        }
    }

    // --- Windows arayüzü ---

    private const int WS_POPUP = unchecked((int)0x80000000), WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000;
    private const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_TABSTOP = 0x00010000, BS_OWNERDRAW = 0x0B;
    private const uint WM_NULL = 0x0000, WM_DESTROY = 0x0002, WM_PAINT = 0x000F, WM_CLOSE = 0x0010, WM_ERASEBKGND = 0x0014;
    private const uint WM_DRAWITEM = 0x002B, WM_SETFONT = 0x0030, WM_COMMAND = 0x0111, DM_GETDEFID = 0x0400;
    private const int DC_HASDEFID = 0x534B;
    private const uint IsBitti = 0x8001;   // WM_APP + 1: bekleme penceresinin işi tamamlandı.
    private const uint ODS_SELECTED = 0x0001, ODS_FOCUS = 0x0010;
    private const uint DT_CENTER = 0x01, DT_VCENTER = 0x04, DT_WORDBREAK = 0x10, DT_SINGLELINE = 0x20, DT_CALCRECT = 0x400, DT_NOPREFIX = 0x800;
    private const int SW_SHOW = 5, SM_CXSCREEN = 0, SM_CYSCREEN = 1, TRANSPARENT = 1, PS_SOLID = 0, DI_NORMAL = 3;
    private const uint SWP_NOZORDER = 0x0004, IMAGE_ICON = 1, LR_DEFAULTSIZE = 0x0040;
    private const uint DEFAULT_CHARSET = 1, CLEARTYPE_QUALITY = 5;
    private static readonly IntPtr IDC_ARROW = 32512;
    private static readonly IntPtr EkranBasinaOlcek = -4;   // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    // .NET, exe'ye gömdüğü uygulama simgesine bu kaynak numarasını verir.
    private static readonly IntPtr UygulamaSimgesi = 32512;

    private delegate IntPtr WndProc(IntPtr pencere, uint ileti, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DRAWITEMSTRUCT
    {
        public uint CtlType, CtlID, itemID, itemAction, itemState;
        public IntPtr hwndItem, hDC;
        public RECT rcItem;
        public IntPtr itemData;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? ad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW sinif);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int ekStil, string sinif, string baslik, int stil, int x, int y, int genislik,
        int yukseklik, IntPtr ust, IntPtr menu, IntPtr modul, IntPtr veri);

    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr pencere, uint ileti, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr pencere);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr pencere, int komut);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr pencere);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr pencere);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr pencere, int kimlik);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG ileti, IntPtr pencere, uint enAz, uint enCok);
    [DllImport("user32.dll")] private static extern bool IsDialogMessageW(IntPtr pencere, ref MSG ileti);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG ileti);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG ileti);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr pencere, uint ileti, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr pencere, uint ileti, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool AdjustWindowRectEx(ref RECT alan, int stil, bool menuVar, int ekStil);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr pencere, IntPtr sonra, int x, int y, int genislik, int yukseklik, uint bayraklar);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int olcu);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr pencere, out RECT alan);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr pencere, out PAINTSTRUCT boyama);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr pencere, ref PAINTSTRUCT boyama);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr pencere);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr pencere, IntPtr dc);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref RECT alan, IntPtr firca);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr ikon, int genislik, int yukseklik, uint adim, IntPtr firca, int bayraklar);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr ikon);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursorW(IntPtr modul, IntPtr ad);
    [DllImport("user32.dll")] private static extern IntPtr LoadImageW(IntPtr modul, IntPtr ad, uint tur, int genislik, int yukseklik, uint bayraklar);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr pencere);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr baglam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(IntPtr dc, string metin, int uzunluk, ref RECT alan, uint bicim);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr pencere, [Out] char[] metin, int enCok);

    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(int renk);
    [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int stil, int kalinlik, int renk);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr nesne);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr nesne);
    [DllImport("gdi32.dll")] private static extern bool RoundRect(IntPtr dc, int sol, int ust, int sag, int alt, int en, int boy);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int kip);
    [DllImport("gdi32.dll")] private static extern int SetTextColor(IntPtr dc, int renk);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int yukseklik, int genislik, int egim, int yon, int kalinlik, uint italik, uint altCizgi,
        uint ustCizgi, uint karakterKumesi, uint cikti, uint kirpma, uint kalite, uint aile, string yuz);
}
