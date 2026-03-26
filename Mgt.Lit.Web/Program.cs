using Mgt.Lit.WebFront.Components;
using Mgt.Lit.WebFront.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Configuration & Services
var apiBaseUrl = builder.Configuration["ApiBaseUrl"]!;

builder.Services.AddScoped(sp =>
    new HttpClient
    {
        BaseAddress = new Uri(apiBaseUrl)
    });

builder.Services.AddScoped<ApiHttpClient>();
builder.Services.AddScoped<AuthService>();

// 2. Add Blazor Services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// 3. Configure HTTP Request Pipeline (ลำดับตรงนี้สำคัญมาก)
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// วาง UseAntiforgery ไว้หลัง StaticFiles และก่อน MapRazorComponents
app.UseAntiforgery();

// 4. Map Components
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();