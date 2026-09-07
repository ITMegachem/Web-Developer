using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.Interfaces;
using Mgt.Lit.Core.Services;
using Mgt.Lit.Core.Services.Dashboard;
using Mgt.Lit.WebApi.Filters;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var publicApiUrl = builder.Configuration["AppUrls:PublicApi"];
// =======================
// Database
// =======================
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql =>
        {
            sql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
            sql.CommandTimeout(60);
        }));

// =======================
// Dependency Injection
// =======================
builder.Services.AddScoped<IUserRepository, UserService>();
//SAP//
builder.Services.AddHttpClient();
builder.Services.AddScoped<SapService>();
builder.Services.AddSingleton<ZohoAuthTokenProvider>();
builder.Services.AddHttpClient<ZohoDealSyncService>();
builder.Services.AddHttpClient<ZohoVisitSyncService>();

// =======================
// JWT Authentication
// =======================
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,         // ✅ ตรวจสอบวันหมดอายุ
            ValidateIssuerSigningKey = true,

            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)
            ),

            // 🔹 เพิ่มบรรทัดนี้: ปกติ JWT จะแถมเวลาให้ 5 นาที (Skew) 
            // ตั้งเป็น Zero เพื่อให้หมดอายุ 30 นาทีเป๊ะตามที่ตั้งค่าไว้
            ClockSkew = TimeSpan.Zero
        };

        // 🔹 เพิ่มส่วนนี้เพื่อเช็ก "Single Session" (ล็อคอินใหม่แล้วของเก่าใช้ไม่ได้)
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                // ✅ ข้าม TokenVersion check สำหรับ API role (เช่น Zoho)
                var role = context.Principal?.FindFirst(ClaimTypes.Role)?.Value;
                if (role == "api")
                    return;

                var cache = context.HttpContext.RequestServices
                    .GetRequiredService<IMemoryCache>();

                var username = context.Principal?.FindFirst(ClaimTypes.Name)?.Value;
                var tokenVersion = context.Principal?.FindFirst("TokenVersion")?.Value;

                var cacheKey = $"tv_{username}";

                if (!cache.TryGetValue(cacheKey, out string? cachedVersion))
                {
                    var dbContext = context.HttpContext.RequestServices
                        .GetRequiredService<AppDbContext>();
                    cachedVersion = await dbContext.MsUsers
                        .AsNoTracking()
                        .Where(u => u.Username == username)
                        .Select(u => u.TokenVersion)
                        .FirstOrDefaultAsync();

                    cache.Set(cacheKey, cachedVersion, TimeSpan.FromMinutes(5));
                }

                if (cachedVersion != tokenVersion)
                    context.Fail("Session invalidated.");
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<IActivityLogService, ActivityLogService>();
builder.Services.AddScoped<ActivityLogFilter>();
builder.Services.AddScoped<ISalesOverviewService, SalesOverviewService>();
builder.Services.AddScoped<IYearlyComparisonService, YearlyComparisonService>();
builder.Services.AddScoped<ISalePerformanceService, SalePerformanceService>();
builder.Services.AddScoped<IProductOverviewService, ProductOverviewService>();
builder.Services.AddScoped<IProductMovementService, ProductMovementService>();
builder.Services.AddScoped<ICustomerOverviewService, CustomerOverviewService>(); 
builder.Services.AddScoped<IBillingDailyService, BillingDailyService>();
builder.Services.AddScoped<IPricingMarginPerformanceService, PricingMarginPerformanceService>();
builder.Services.AddScoped<ICustomerChurnAnalysisService, CustomerChurnAnalysisService>();
builder.Services.AddScoped<IOpportunityWinRateService, OpportunityWinRateService>();
builder.Services.AddScoped<IAverageDaysToCloseService, AverageDaysToCloseService>();
builder.Services.AddScoped<ICrossSellUpsellGainsService, CrossSellUpsellGainsService>();
builder.Services.AddScoped<ISalesForecastAccuracyService, SalesForecastAccuracyService>();
builder.Services.AddScoped<IVisitDailyReportService, VisitDailyReportService>();
builder.Services.AddScoped<ISalesProductivityService, SalesProductivityService>();
builder.Services.AddScoped<IForecastMonthlyReportService, ForecastMonthlyReportService>();
builder.Services.AddHttpClient();
// =======================
// CORS
// =======================
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy
            .WithOrigins(
                "https://localhost:7092",
                "https://onereport.megachem.co.th"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
// =======================
// Controllers & Swagger
// =======================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
var app = builder.Build();

// =======================
// Middleware Pipeline
// =======================
//app.UseSwagger();
//app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseCors("FrontendPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
