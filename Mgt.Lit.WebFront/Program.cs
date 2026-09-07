
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
// ★★★ Fix (2026-09): เพิ่ม .AddHttpMessageHandler<AuthTokenHandler>() ให้ Dash*ApiService ทุกตัว ★★★
// เดิมไม่มีตัวไหนผ่าน handler นี้เลย (ต่างจาก ApiHttpClient/StockService/ฯลฯ ด้านบนที่มี) ทำให้พอ JWT หมดอายุ
// (30 นาที) รายงาน Dashboard ทุกหน้าจะโยน HttpRequestException 401 ตรงๆ แทนที่จะ refresh token แล้ว retry ให้อัตโนมัติ
// เหมือนส่วนอื่นของแอป — Dash*ApiService เองยังคง attach token เริ่มต้นเองอยู่ (ไม่ผิดอะไร ซ้ำซ้อนแต่ไม่ชนกัน)
// handler จะเป็นคนจับ 401 แล้ว refresh + แนบ token ใหม่ retry ให้แทน
builder.Services.AddHttpClient<DashSalesOverviewApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
}).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashYearlyComparisonApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
}).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashSalePerformanceApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
}).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashProductOverviewApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
}).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashProductMovementApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
}).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashCustomerOverviewApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
}).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashBillingDailyApiService>(c =>
{ c.BaseAddress = new Uri(apiBaseUrl!); }
).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashPricingMarginPerformanceApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
}).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashCustomerChurnAnalysisApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
}).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashOpportunityWinRateApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
}).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashAverageDaysToCloseApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
}).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashCrossSellUpsellGainsApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
}).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashSalesForecastAccuracyApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
}).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashVisitDailyReportApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
}).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashSalesProductivityApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
}).AddHttpMessageHandler<AuthTokenHandler>();
builder.Services.AddHttpClient<DashForecastMonthlyApiService>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl!);
    c.Timeout = TimeSpan.FromSeconds(120); // ★ เรียก SAP live ต่อ Material Code หลายตัวแบบขนาน อาจใช้เวลานานกว่ารายงานอื่น
}).AddHttpMessageHandler<AuthTokenHandler>();
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