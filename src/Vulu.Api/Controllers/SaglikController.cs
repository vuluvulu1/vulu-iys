using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SaglikController : ControllerBase
{
    private static readonly TimeSpan PingZamanAsimi = TimeSpan.FromSeconds(2);

    private readonly IMongoDatabase _db;

    public SaglikController(IMongoDatabase db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<SaglikYaniti>> Getir(CancellationToken iptal)
    {
        using var sure = CancellationTokenSource.CreateLinkedTokenSource(iptal);
        sure.CancelAfter(PingZamanAsimi);

        try
        {
            await _db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: sure.Token);
            return Ok(new SaglikYaniti(true));
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException or OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new SaglikYaniti(false, "Veritabanına ulaşılamıyor."));
        }
    }
}
