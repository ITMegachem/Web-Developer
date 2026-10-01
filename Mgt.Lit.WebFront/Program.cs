using Microsoft.AspNetCore.DataProtection;

using Blazorise;
using Blazorise.Bootstrap5;
using Blazorise.Charts;
using Blazorise.Icons.FontAwesome;
using Mgt.Lit.WebFront.Auth;
using Mgt.Lit.WebFront.Components;
using Mgt.Lit.WebFront.Services;
using Mgt.Lit.WebFront.Services.DashBoard;
using Mgt.Lit.WebFront.Services.Member;
using Mgt.Lit.WebFront.Services.SalesOrder;

var builder = WebApplication.CreateBuilder(args);

var apiBaseUrl = builder.Environment.IsDevelopment()
    ? builder.Configuration["ApiBaseUrlDev"]
    : builder.Configuration["ApiBaseUrlDocker"];

// ✅ AuthState ต้องมาก่อน เพราะ AuthTokenHandler inject AuthState
builder.Services.AddScoped<AuthState>();
builder.Services.AddSingleton<SessionBroadcast>();   // instant "check session" between tabs/devices
builder.Services.AddScoped<UiState>();

// ✅ AuthTokenHandler — ลบ IHttpContextAccessor ออกแล้ว
builder.Services.AddScoped<AuthTokenHandler>();

// ❌ ลบบรรทัดนี้ออก — ไม่ต้องใช้แล้ว
// builder.Services.AddHttpContextAccessor();

// ✅ AuthService — ไม่ผ่าน Handler (ทำ login/refresh/logout เอง)
builder.Services.AddHttpClient<AuthService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl!);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ✅ RefreshClient — ไม่ผ่าน Handler (กัน infinite loop)
// ไม่ต้องใช้ CookieContainer แล้ว เพราะส่ง token ใน body
builder.Services.AddHttpClient("RefreshClient", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl!);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// ✅ Services ที่ผ่าน AuthTokenHandler
builder.Services.AddHttpClient<StockService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl!);
    client.Timeout = TimeSpan.FromSeconds(60);
})
.AddHttpMessageHandler<AuthTokenHandler>();

builder.Services.AddHttpClient<SalesOrderService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl!);
    client.Timeout = TimeSpan.FromSeconds(60);
})
.AddHttpMessageHandler<AuthTokenHandler>();

builder.Services.AddHttpClient<NofReportService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl!);
    client.Timeout = TimeSpan.FromSeconds(60);
})
.AddHttpMessageHandler<AuthTokenHandler>();

builder.Services.AddHttpClient<ApiHttpClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl!);
    client.Timeout = TimeSpan.FromSeconds(60);
})
.AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashSalesOverviewApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
});
builder.Services.AddHttpClient<DashYearlyComparisonApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
});
builder.Services.AddHttpClient<DashSalePerformanceApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
});
builder.Services.AddHttpClient<DashProductOverviewApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
});
builder.Services.AddHttpClient<DashProductMovementApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
});
builder.Services.AddHttpClient<DashCustomerOverviewApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
});
builder.Services.AddHttpClient<DashBillingDailyApiService>(c =>
{ c.BaseAddress = new Uri(apiBaseUrl!); }
);
// ✅ Services อื่น
builder.Services.AddScoped<IAccountService, AccountService>();

builder.Services.AddScoped<ISalesOrderService>(sp =>
    sp.GetRequiredService<SalesOrderService>());
builder.Services.AddScoped<INofReportService>(sp =>
    sp.GetRequiredService<NofReportService>());

builder.Services.AddScoped<DownloadLogService>();
builder.Services.AddScoped<PageContext>();
builder.Services.AddScoped<LoggingHeaderHandler>();
//builder.Services.AddApexCharts();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        options.ClientTimeoutInterval = TimeSpan.FromMinutes(10);
        options.KeepAliveInterval = TimeSpan.FromMinutes(3);
        options.HandshakeTimeout = TimeSpan.FromSeconds(30);
        options.MaximumReceiveMessageSize = 102400;
    });
builder.Services
    .AddBlazorise(options =>
    {
        options.Immediate = true;
    })
    .AddBootstrap5Providers()
    .AddFontAwesomeIcons();
// ✅ เพิ่มต่อจาก named clients อื่น
builder.Services.AddHttpClient("ApiClient", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl!);
    client.Timeout = TimeSpan.FromSeconds(60);
})
.AddHttpMessageHandler<AuthTokenHandler>();
// ⚠️ Every HttpClient here runs on the SERVER and IHttpClientFactory pools its handlers across ALL
// users. With cookies on (the default), one user's refreshToken cookie would be stored in the shared
// jar and sent with another user's /refresh → that user gets the first user's token. Cookies OFF:
// the refresh token travels explicitly in the body (refresh-explicit / logout).
builder.Services.ConfigureHttpClientDefaults(b =>
    b.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false }));

// ProtectedLocalStorage is encrypted with Data Protection keys. Persist them (mount a volume in
// Docker) or every redeploy makes stored logins unreadable → everyone is signed out.
var dpKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dpKeysPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("Mgt.Lit.WebFront")
        .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath));
}

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