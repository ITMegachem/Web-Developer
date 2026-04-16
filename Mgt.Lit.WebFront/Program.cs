using Mgt.Lit.WebFront.Auth;
using Mgt.Lit.WebFront.Components;
using Mgt.Lit.WebFront.Services;
using Mgt.Lit.WebFront.Services.Member;
using Mgt.Lit.WebFront.Services.SalesOrder;
using Blazorise;
using Blazorise.Bootstrap5;
using Blazorise.Icons.FontAwesome;
using Blazorise.Charts;

var builder = WebApplication.CreateBuilder(args);

var apiBaseUrl = builder.Environment.IsDevelopment()
    ? builder.Configuration["ApiBaseUrlDev"]
    : builder.Configuration["ApiBaseUrlDocker"];

// ✅ 1. ลงทะเบียน AuthTokenHandler ก่อน (ต้องมาก่อนที่จะ AddHttpMessageHandler)
builder.Services.AddScoped<AuthTokenHandler>();
builder.Services.AddHttpContextAccessor();
// ✅ 2. AuthService — ไม่ผ่าน Handler (ทำ login/refresh/logout เอง)
builder.Services.AddHttpClient<AuthService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

// ✅ RefreshClient — ไม่ผ่าน Handler (กัน infinite loop)
builder.Services.AddHttpClient("RefreshClient", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    UseCookies = true,
    CookieContainer = new System.Net.CookieContainer()
});
// ✅ StockService ผ่าน AuthTokenHandler
builder.Services.AddHttpClient<StockService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
})
.AddHttpMessageHandler<AuthTokenHandler>();

// ✅ SalesOrderService ผ่าน AuthTokenHandler
builder.Services.AddHttpClient<SalesOrderService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
})
.AddHttpMessageHandler<AuthTokenHandler>();

// ✅ NofReportService ผ่าน AuthTokenHandler
builder.Services.AddHttpClient<NofReportService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
})
.AddHttpMessageHandler<AuthTokenHandler>();

// ✅ 3. AuthState ต้องมาก่อน AuthService (เพราะ AuthService inject AuthState)
builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped<UiState>();
builder.Services.AddScoped<ApiHttpClient>();
builder.Services.AddScoped<IAccountService, AccountService>();

// ❌ ลบออก — AddHttpClient<SalesOrderService> ลงทะเบียนแล้ว ห้าม AddScoped ซ้ำ
// builder.Services.AddScoped<ISalesOrderService, SalesOrderService>();
// ❌ ลบออก — AddHttpClient<NofReportService> ลงทะเบียนแล้ว ห้าม AddScoped ซ้ำ
// builder.Services.AddScoped<INofReportService, NofReportService>();

// ✅ ลง Interface mapping แทน
builder.Services.AddScoped<ISalesOrderService>(sp =>
    sp.GetRequiredService<SalesOrderService>());
builder.Services.AddScoped<INofReportService>(sp =>
    sp.GetRequiredService<NofReportService>());

builder.Services.AddScoped<DownloadLogService>();
builder.Services.AddScoped<PageContext>();
builder.Services.AddScoped<LoggingHeaderHandler>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services
    .AddBlazorise(options =>
    {
        options.Immediate = true;
    })
    .AddBootstrap5Providers()
    .AddFontAwesomeIcons();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();