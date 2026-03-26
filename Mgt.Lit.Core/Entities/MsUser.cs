using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Mgt.Lit.Core.Entities
{
    [Table("Ms_User")]
    public class MsUser
    {
        [Key]
        public int? UserID { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? FullName { get; set; }
        public string? UserRole { get; set; }
        public bool IsActive { get; set; }
        public string? Division { get; set; }
        public string? TokenVersion { get; set; }
    }
    [Table("Ms_Company")]
    public class MsCompany
    {
        [Key] // 👈 สำคัญมาก
        public int CompanyID { get; set; }

        public string CompanyCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
    }
    public class MsUserCompany
    {
        public int UserID { get; set; }
        public int CompanyID { get; set; }
        public bool IsPrimary { get; set; }

        // (optional navigation)
        public MsUser? User { get; set; }
        public MsCompany? Company { get; set; }
    }

    [Table("RefreshTokens")]
    public class RefreshToken
    {
        [Key]
        public int RefreshTokenID { get; set; }

        public int UserID { get; set; }
        public string Token { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
        public bool IsRevoked { get; set; }

        public DateTime? CreatedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }

        public int? CurrentCompanyID { get; set; }

        public MsUser User { get; set; } = null!;
    }
    public class ActivityLog
    {
        public int Id { get; set; }
        public Guid SystemID { get; set; }
        public int UserID { get; set; }
        public string Username { get; set; }
        public int? CompanyID { get; set; }
        public string Menu { get; set; }
        public string Page { get; set; }
        public string Action { get; set; }

        public int? ExecutionTimeMs { get; set; }
        public string IpAddress { get; set; }
        public int? StatusCode { get; set; }
        public bool? IsSuccess { get; set; }

        public DateTime CreateDate { get; set; }
    }
    public class ErrorLog
    {
        public int Id { get; set; }
        public int? UserID { get; set; }
        public string? Username { get; set; }
        public string? Controller { get; set; }
        public string? Action { get; set; }
        public string ErrorMessage { get; set; }
        public string? StackTrace { get; set; }
        public DateTime CreateDate { get; set; }
    }
}
