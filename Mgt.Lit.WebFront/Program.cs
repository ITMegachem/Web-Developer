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


builder.Services.AddScoped<DownloadLogService>();
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(apiBaseUrl)
});

// Auth/State
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuthState>();

// UI
builder.Services.AddScoped<UiState>();

// Member
builder.Services.AddScoped<ApiHttpClient>();
builder.Services.AddScoped<IAccountService, AccountService>();

// Other services
builder.Services.AddScoped<ISalesOrderService, SalesOrderService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services
    .AddBlazorise(options =>
    {
        options.Immediate = true;
    })
    .AddBootstrap5Providers()
    .AddFontAwesomeIcons();

builder.Services.AddHttpClient<StockService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

builder.Services.AddScoped<PageContext>();
builder.Services.AddScoped<LoggingHeaderHandler>();

builder.Services.AddHttpClient("API")
    .AddHttpMessageHandler<LoggingHeaderHandler>();
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
