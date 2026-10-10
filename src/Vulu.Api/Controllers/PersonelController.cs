using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Vulu.Api.Veri;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

// İş atanabilecek kişilerin seçim listesi. Hesap yönetiminden (KullanicilarController) bilerek ayrıdır:
// o controller yalnızca yöneticiye açıktır, bu liste ise giriş yapmış herkese ve yalnızca ad ile uzmanlığı verir.
[ApiController]
[Route("api/personel")]
public class PersonelController : ControllerBase
{
    private readonly MongoBaglami _db;

    public PersonelController(MongoBaglami db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<PersonelOzeti>>> Listele(CancellationToken iptal)
    {
        var aktifler = await _db.Kullanicilar
            .Find(k => k.AktifMi, new FindOptions { Collation = MongoBaglami.TurkceSiralama })
            .SortBy(k => k.AdSoyad)
            .ToListAsync(iptal);
        return aktifler.Select(k => new PersonelOzeti(k.Id, k.AdSoyad, k.Uzmanlik)).ToList();
    }
}
