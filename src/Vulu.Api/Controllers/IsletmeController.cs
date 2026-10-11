using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Kimlik;
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
    private readonly Oturumlar _oturumlar;

    public IsletmeController(PlatformBaglami platform, GecerliIsletme gecerli, Oturumlar oturumlar)
    {
        _platform = platform;
        _gecerli = gecerli;
        _oturumlar = oturumlar;
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
            .Set(i => i.KdvOrani, istek.KdvOrani)
            .Set(i => i.ServisKosullari, Metin.BosIseNull(istek.ServisKosullari))
            .Set(i => i.SaklamaYili, istek.SaklamaYili)
            .Set(i => i.AydinlatmaMetni, Metin.BosIseNull(istek.AydinlatmaMetni));

        if (istek.LogoyuKaldir)
        {
            guncelleme = guncelleme.Unset(i => i.Logo).Unset(i => i.LogoTuru);
        }
        else if (!string.IsNullOrEmpty(istek.LogoVerisi))
        {
            if (LogoDogrulama.Coz(istek.LogoVerisi, istek.LogoTuru, out var logo) is { } hata)
                return Problem(detail: hata, statusCode: StatusCodes.Status400BadRequest);
            guncelleme = guncelleme.Set(i => i.Logo, logo).Set(i => i.LogoTuru, istek.LogoTuru);
        }

        // Yalnızca profil alanları yazılır; kod, veritabanı adı ve askı durumu bu uçtan değiştirilemez.
        var guncel = await _platform.Isletmeler.FindOneAndUpdateAsync<Isletme>(i => i.Id == isletme.Id, guncelleme,
            new FindOneAndUpdateOptions<Isletme> { ReturnDocument = ReturnDocument.After }, iptal);

        _oturumlar.IsletmeDegisti(isletme.Id);
        return guncel.YanitaCevir();
    }
}

public static class IsletmeDonusumleri
{
    public static IsletmeYaniti YanitaCevir(this Isletme i, long? personelSayisi = null) => new(
        i.Id, i.Kod, i.Ad, i.Adres, i.Telefon, i.Eposta, i.VergiDairesi, i.VergiNo, i.KdvOrani, i.AktifMi,
        i.OlusturmaTarihi, i.Logo is null ? null : Convert.ToBase64String(i.Logo), i.LogoTuru,
        personelSayisi, i.ServisKosullari, i.SaklamaYili, i.AydinlatmaMetni);
}
