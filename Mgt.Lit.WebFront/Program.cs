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

// ✅ Services อื่น
builder.Services.AddScoped<IAccountService, AccountService>();

builder.Services.AddScoped<ISalesOrderService>(sp =>
    sp.GetRequiredService<SalesOrderService>());
builder.Services.AddScoped<INofReportService>(sp =>
    sp.GetRequiredService<NofReportService>());

builder.Services.AddScoped<DownloadLogService>();
builder.Services.AddScoped<PageContext>();
builder.Services.AddScoped<LoggingHeaderHandler>();

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