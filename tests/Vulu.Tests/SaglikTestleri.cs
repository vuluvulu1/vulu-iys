using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Vulu.Shared;

namespace Vulu.Tests;

public class SaglikTestleri
{
    [Fact]
    public async Task Veritabani_ulasilamazsa_503_doner()
    {
        await using var fabrika = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Mongo:BaglantiDizesi", "mongodb://127.0.0.1:1");
            b.UseSetting("Mongo:VeritabaniAdi", "vulu_tsys_test");
            b.UseSetting("Jwt:Anahtar", ApiFabrikasi.JwtAnahtari);
        });
        using var istemci = fabrika.CreateClient();

        using var yanit = await istemci.GetAsync("api/saglik");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, yanit.StatusCode);
        var govde = await yanit.Content.ReadFromJsonAsync<SaglikYaniti>();
        Assert.NotNull(govde);
        Assert.False(govde.VeritabaniErisilebilir);
    }
}
