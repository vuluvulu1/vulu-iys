using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using Vulu.Api.Kimlik;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

[ApiController]
[Route("api/saglik")]
public class SaglikController : ControllerBase
{
    private static readonly TimeSpan PingZamanAsimi = TimeSpan.FromSeconds(2);

    private readonly IMongoDatabase _db;

    public SaglikController(IMongoDatabase db) => _db = db;

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<SaglikYaniti>> Getir(CancellationToken iptal)
    {
        using var sure = CancellationTokenSource.CreateLinkedTokenSource(iptal);
        sure.CancelAfter(PingZamanAsimi);

        try
        {
            await _db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: sure.Token);
            return Ok(new SaglikYaniti(true, null, ApiSozlesmesi.Surum));
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException or OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new SaglikYaniti(false, "Veritabanına ulaşılamıyor.", ApiSozlesmesi.Surum));
        }
    }

    // Barındırma platformunun sağlık kontrolü içindir: veritabanı kapalıyken de 200 döner,
    // çünkü o durumda API'yi yeniden başlatmak sorunu çözmez.
    [HttpGet("canli")]
    [AllowAnonymous]
    public IActionResult Canli() => Ok();

    // API'nin bu isteği nasıl gördüğü: vulu yöneticisi, canlı ortamda vekil ayarının (Vekil:AtlamaSayisi) doğru
    // olduğunu buradan denetler. IstemciAdresi kendi internet adresi olmalıdır; vekilin adresi görünüyorsa bütün
    // kullanıcılar tek adres sayılır ve adres başına sınırlar herkesi birden etkiler.
    [HttpGet("baglanti")]
    [Authorize(Roles = TokenServisi.PlatformRolu)]
    public ActionResult<BaglantiYaniti> Baglanti() => new BaglantiYaniti(
        HttpContext.Connection.RemoteIpAddress?.ToString(),
        Request.IsHttps,
        Request.Headers["X-Forwarded-For"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Length);
}
