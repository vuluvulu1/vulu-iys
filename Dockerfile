# API'nin barındırma imajı (Render vb.). Masaüstü uygulaması bu imaja girmez.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS derleme
WORKDIR /kaynak

COPY src/Vulu.Shared/Vulu.Shared.csproj src/Vulu.Shared/
COPY src/Vulu.Api/Vulu.Api.csproj src/Vulu.Api/
RUN dotnet restore src/Vulu.Api/Vulu.Api.csproj

COPY assets/logo.svg assets/
COPY src/Vulu.Shared/ src/Vulu.Shared/
COPY src/Vulu.Api/ src/Vulu.Api/
RUN dotnet publish src/Vulu.Api/Vulu.Api.csproj -c Release -o /uygulama --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /uygulama
COPY --from=derleme /uygulama .

# API bir ters vekilin arkasında çalışır: gerçek istemci adresi ve HTTPS bilgisi X-Forwarded-* başlıklarından okunur.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    ASPNETCORE_HTTPS_PORT=443

USER $APP_UID

# Platform dinlenecek kapıyı PORT değişkeniyle verir.
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet Vulu.Api.dll"]
