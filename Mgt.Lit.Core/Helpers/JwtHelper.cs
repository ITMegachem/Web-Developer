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
            List<UserCompanyDto>? allCompanies = null)  // ✅ เพิ่ม parameter นี้
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

            // ✅ เพิ่ม Company claim ทุกบริษัทที่ user เข้าถึงได้
            if (allCompanies != null)
            {
                foreach (var c in allCompanies)
                    claims.Add(new Claim("Company", c.CompanyCode));
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config["Jwt:Key"]!));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}