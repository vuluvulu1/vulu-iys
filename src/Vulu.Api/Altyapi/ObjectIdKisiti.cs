using MongoDB.Bson;

namespace Vulu.Api.Altyapi;

// Rotalarda {id:oid} olarak kullanılır; geçersiz kimlikler sürücüye ulaşmadan 404 olur.
public class ObjectIdKisiti : IRouteConstraint
{
    public const string Ad = "oid";

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey,
        RouteValueDictionary values, RouteDirection routeDirection) =>
        values.TryGetValue(routeKey, out var deger) && ObjectId.TryParse(deger?.ToString(), out _);
}
