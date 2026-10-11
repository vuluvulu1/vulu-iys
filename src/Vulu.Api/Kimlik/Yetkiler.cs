using Microsoft.AspNetCore.Authorization;
using Vulu.Shared;

namespace Vulu.Api.Kimlik;

// Rolün yanında kişiye ayrı ayrı verilebilen izinler.
public static class Yetkiler
{
    // Stok kartı açma, düzenleme, silme ve stok girişi: yöneticiler ve stok yetkisi verilmiş personel.
    public const string StokYonetimi = nameof(StokYonetimi);

    public static AuthorizationBuilder YetkileriEkle(this AuthorizationBuilder yetkilendirme) =>
        yetkilendirme.AddPolicy(StokYonetimi, politika => politika
            .RequireClaim(TokenServisi.IsletmeTalebi)
            .RequireAssertion(baglam => baglam.User.IsInRole(nameof(KullaniciRol.Admin))
                || baglam.User.HasClaim(talep => talep.Type == TokenServisi.StokTalebi)));
}
