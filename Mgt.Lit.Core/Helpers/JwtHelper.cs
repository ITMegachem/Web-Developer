using Mgt.Lit.Core.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Mgt.Lit.Core.Helpers
{
    public static class JwtHelper
    {
        public static string GenerateToken(MsUser user, IConfiguration config, int? primaryCompanyId = null, string? primaryCompanyCode = null, string? tokenVersion = null)
        {
            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.Username ?? ""),
        new Claim(ClaimTypes.Role, user.UserRole ?? ""),
        new Claim("FullName", user.FullName ?? ""),
        new Claim("Division", user.Division ?? ""),
        new Claim("CompanyID", (primaryCompanyId ?? 0).ToString()),
        // 🔹 ใส่ Version เข้าไปใน Claim เพื่อเอาไว้เช็กใน Middleware
       new Claim("TokenVersion", user.TokenVersion ?? ""),
       new Claim("UserID", user.UserID.ToString())  // 🔥 เพิ่มอันนี้
    };

            if (!string.IsNullOrWhiteSpace(primaryCompanyCode))
            {
                claims.Add(new Claim("CompanyCode", primaryCompanyCode));
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config["Jwt:Key"]!)
            );

            var creds = new SigningCredentials(
                key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
        issuer: config["Jwt:Issuer"],
        audience: config["Jwt:Audience"],
        claims: claims,
        // 🔹 ตั้งให้หมดอายุใน 30 นาที (หรือดึงจาก Config)
        expires: DateTime.UtcNow.AddMinutes(30),
        signingCredentials: creds
    );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
