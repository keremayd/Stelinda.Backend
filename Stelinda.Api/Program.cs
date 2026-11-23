using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Stelinda.Api.Filters;
using AwsService.Extensions;
using Common.Options;
using Stelinda.Api.Middlewares;
using Postgres.Extensions;
using Service.Extensions;
using Service.Handlers.Queries;

Console.WriteLine("[startup] entering Program");
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
Console.WriteLine("[startup] builder created");

// Yerel kimlik bilgileri Git'e girmez; ortam ve komut satırı önceliği korunur.
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

// Add services to the container.
builder.Services.AddIdentity();
Console.WriteLine("[startup] identity registered");
builder.Services.AddJwt(builder.Configuration);
Console.WriteLine("[startup] jwt registered");
builder.Services
    .AddPostgres(builder.Configuration)
    .AddAwsS3Service(builder.Configuration);
Console.WriteLine("[startup] postgres/aws registered");

// İçerik girişi ucunun paylaşılan anahtarı. Bölüm yoksa anahtar boş kalır ve
// uç kendini kapatır (bkz. KartyAdminOptions).
builder.Services.Configure<KartyAdminOptions>(
    builder.Configuration.GetSection(KartyAdminOptions.SectionName));

// Apple ile giriş: token'ın alıcısı paket kimliği; Apple'ın anahtarları
// bellekte tutulur, bu yüzden doğrulayıcı tek örnek.
builder.Services.Configure<AppleOptions>(
    builder.Configuration.GetSection(AppleOptions.SectionName));
builder.Services.AddSingleton<Service.Helpers.AppleIdentityValidator>();
// Hesap silinirken Apple'daki izni geri alır; anahtar appsettings.Local.json'da.
builder.Services.AddSingleton<Service.Helpers.AppleTokenRevoker>();

// CORS kalktı: API'yi tarayıcıdan çağıran bir istemci yok (uygulama ve
// site); her kaynağa açık CORS gereksiz bir kapıydı.

// Her uç oturum ister; açık olanlar [AllowAnonymous] ile işaretli (giriş,
// Apple bildirimi, kart yönetimi). Önceden [Authorize] unutulan uçlar
// kimliksiz çalışıyordu.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// Giriş uçlarına IP başına dakikada 20 istek: parola denemesini yavaşlatır.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("authentication", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "bilinmiyor",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

builder.Services.AddControllers(options =>
    // İstek yalnız oturumdaki kullanıcı adına (bkz. OwnUserFilter).
    options.Filters.Add<OwnUserFilter>());
Console.WriteLine("[startup] controllers registered");

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
Console.WriteLine("[startup] swagger registered");
builder.Services.AddMediatR(cfg=>cfg.RegisterServicesFromAssemblies(typeof(GetUserByIdQueryHandler).Assembly));
Console.WriteLine("[startup] mediatR registered");


var app = builder.Build();
Console.WriteLine("[startup] app built");

// Cloud Run önündeki Google yük dengeleyicisi istemcinin adresini
// X-Forwarded-For'un sonuna ekler; deneme sınırı gerçek adrese göre işlesin.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1,
};
forwardedHeaders.KnownNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

// Configure the HTTP byIdRequest pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
Console.WriteLine("[startup] controllers mapped");

app.Run();
