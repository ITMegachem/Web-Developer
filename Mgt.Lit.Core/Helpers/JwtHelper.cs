using Mgt.Lit.Core.DTOs;  // ← UserCompanyDto
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
        public static string GenerateToken(
    MsUser user,
    IConfiguration config,
    int? primaryCompanyId = null,
    string? primaryCompanyCode = null,
    string? tokenVersion = null,
    List<UserCompanyDto>? allCompanies = null,
    UserPermissionDto? permission = null)  // ✅ เพิ่ม parameter
        {
            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.Username ?? ""),
        new Claim(ClaimTypes.Role, user.UserRole ?? ""),
        new Claim("FullName", user.FullName ?? ""),
        new Claim("Division", user.Division ?? ""),
        new Claim("CompanyID", (primaryCompanyId ?? 0).ToString()),
        new Claim("TokenVersion", user.TokenVersion ?? ""),
        new Claim("UserID", user.UserID.ToString())
    };

            if (!string.IsNullOrWhiteSpace(primaryCompanyCode))
                claims.Add(new Claim("CompanyCode", primaryCompanyCode));

            if (allCompanies != null)
                foreach (var c in allCompanies)
                    claims.Add(new Claim("Company", c.CompanyCode));

            // ✅ เพิ่ม permission claims เข้า JWT
            if (permission != null)
            {
                claims.Add(new Claim("Page1Access", permission.Page1Access ? "true" : "false"));
                claims.Add(new Claim("Page2Access", permission.Page2Access ? "true" : "false"));
                claims.Add(new Claim("Page3Access", permission.Page3Access ? "true" : "false"));
                claims.Add(new Claim("Page4Access", permission.Page4Access ? "true" : "false"));
                claims.Add(new Claim("DataScope", permission.DataScope ?? "OWN"));
                claims.Add(new Claim("CanViewVendor", permission.CanViewVendor ? "true" : "false"));
                claims.Add(new Claim("CanViewCost", permission.CanViewCost ? "true" : "false"));
                claims.Add(new Claim("CanViewCustomer", permission.CanViewCustomer ? "true" : "false"));
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(
    double.Parse(config["Jwt:ExpireMinutes"] ?? "60")),
                signingCredentials: creds);
            // JwtHelper.cs — ตรวจสอบว่าอ่านค่าจาก config จริง
            var expireMinutes = int.Parse(config["Jwt:ExpireMinutes"] ?? "60");
            var expiry = DateTime.UtcNow.AddMinutes(expireMinutes);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}