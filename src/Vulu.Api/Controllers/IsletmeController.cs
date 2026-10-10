using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

// Oturumdaki personelin kendi işletmesinin bilgileri. Herkes görür, yalnızca işletme yöneticisi düzenler.
[ApiController]
[Route("api/isletme")]
public class IsletmeController : ControllerBase
{
    private readonly PlatformBaglami _platform;
    private readonly GecerliIsletme _gecerli;
    private readonly IMemoryCache _onbellek;

    public IsletmeController(PlatformBaglami platform, GecerliIsletme gecerli, IMemoryCache onbellek)
    {
        _platform = platform;
        _gecerli = gecerli;
        _onbellek = onbellek;
    }

    [HttpGet]
    public ActionResult<IsletmeYaniti> Getir() => _gecerli.Zorunlu.YanitaCevir();

    [HttpPut]
    [Authorize(Roles = nameof(KullaniciRol.Admin))]
    public async Task<ActionResult<IsletmeYaniti>> Guncelle(IsletmeProfilIstegi istek, CancellationToken iptal)
    {
        var isletme = _gecerli.Zorunlu;

        var guncelleme = Builders<Isletme>.Update
            .Set(i => i.Ad, istek.Ad.Trim())
            .Set(i => i.Adres, Metin.BosIseNull(istek.Adres))
            .Set(i => i.Telefon, Metin.BosIseNull(istek.Telefon))
            .Set(i => i.Eposta, Metin.BosIseNull(istek.Eposta))
            .Set(i => i.VergiDairesi, Metin.BosIseNull(istek.VergiDairesi))
            .Set(i => i.VergiNo, Metin.BosIseNull(istek.VergiNo))
            .Set(i => i.KdvOrani, istek.KdvOrani);

        if (istek.LogoyuKaldir)
        {
            guncelleme = guncelleme.Unset(i => i.Logo).Unset(i => i.LogoTuru);
        }
        else if (!string.IsNullOrEmpty(istek.LogoVerisi))
        {
            if (LogoyuCoz(istek.LogoVerisi, istek.LogoTuru, out var logo) is { } hata)
                return Problem(detail: hata, statusCode: StatusCodes.Status400BadRequest);
            guncelleme = guncelleme.Set(i => i.Logo, logo).Set(i => i.LogoTuru, istek.LogoTuru);
        }

        // Yalnızca profil alanları yazılır; kod, veritabanı adı ve askı durumu bu uçtan değiştirilemez.
        var guncel = await _platform.Isletmeler.FindOneAndUpdateAsync<Isletme>(i => i.Id == isletme.Id, guncelleme,
            new FindOneAndUpdateOptions<Isletme> { ReturnDocument = ReturnDocument.After }, iptal);

        IsletmeCozucu.OnbellektenCikar(_onbellek, isletme.Id);
        return guncel.YanitaCevir();
    }

    // Logoyu çözer ve gerçekten bildirilen türde bir resim olduğunu dosya imzasından doğrular.
    private static string? LogoyuCoz(string veri, string? tur, out byte[] logo)
    {
        logo = [];

        if (tur is null || !IsletmeKurallari.LogoTurleri.Contains(tur))
            return "Logo PNG ya da JPEG olmalı.";

        try
        {
            logo = Convert.FromBase64String(veri);
        }
        catch (FormatException)
        {
            return "Logo verisi okunamadı.";
        }

        if (logo.Length > IsletmeKurallari.EnFazlaLogoBayt)
            return $"Logo en fazla {IsletmeKurallari.EnFazlaLogoBayt / 1024} KB olabilir.";

        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        ReadOnlySpan<byte> jpeg = [0xFF, 0xD8, 0xFF];
        var imzaTutuyor = tur == "image/png" ? logo.AsSpan().StartsWith(png) : logo.AsSpan().StartsWith(jpeg);
        return imzaTutuyor ? null : "Logo dosyası bildirilen türde bir resim değil.";
    }
}

public static class IsletmeDonusumleri
{
    public static IsletmeYaniti YanitaCevir(this Isletme i, long? personelSayisi = null) => new(
        i.Id, i.Kod, i.Ad, i.Adres, i.Telefon, i.Eposta, i.VergiDairesi, i.VergiNo, i.KdvOrani, i.AktifMi,
        i.OlusturmaTarihi, i.Logo is null ? null : Convert.ToBase64String(i.Logo), i.LogoTuru,
        i.PersonelSiniri, personelSayisi);
}
