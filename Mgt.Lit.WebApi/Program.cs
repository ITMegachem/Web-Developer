using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.Interfaces;
using Mgt.Lit.Core.Services;
using Mgt.Lit.WebApi.Filters;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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
        builder.Configuration.GetConnectionString("DefaultConnection"))
);

// =======================
// Dependency Injection
// =======================
builder.Services.AddScoped<IUserRepository, UserService>();
//SAP//
builder.Services.AddHttpClient();
builder.Services.AddScoped<SapService>();

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
                var dbContext = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

                // 1. ดึง Username และ TokenVersion จาก Claim ที่เราใส่ไว้ใน Token
                var username = context.Principal?.FindFirst(ClaimTypes.Name)?.Value;
                var tokenVersion = context.Principal?.FindFirst("TokenVersion")?.Value;

                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(tokenVersion))
                {
                    context.Fail("Unauthorized: Missing token version.");
                    return;
                }

                // 2. ไปเช็กใน Database ว่าตอนนี้ User คนนี้มี Version ตรงกับในมือไหม
                var userVersionInDb = await dbContext.MsUsers
                    .AsNoTracking()
                    .Where(u => u.Username == username)
                    .Select(u => u.TokenVersion) // สมมติว่ามี Column นี้ในตารางนะคะ
                    .FirstOrDefaultAsync();

                // 3. ถ้าไม่ตรงกัน แปลว่ามีการ Login ใหม่ไปแล้ว
                if (userVersionInDb != tokenVersion)
                {
                    context.Fail("Unauthorized: This session has been invalidated by a new login.");
                }
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<IActivityLogService, ActivityLogService>();
builder.Services.AddScoped<ActivityLogFilter>();
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
