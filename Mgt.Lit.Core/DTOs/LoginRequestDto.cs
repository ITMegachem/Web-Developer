using Mgt.Lit.Core.Entities;

namespace Mgt.Lit.Core.DTOs
{
    public class LoginRequestDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class LoginResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public int UserID { get; set;}
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string UserRole { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;

        public int? PrimaryCompanyID { get; set; }
        public string? PrimaryCompanyCode { get; set; }

        public List<UserCompanyDto> Companies { get; set; } = new();
        public UserPermissionDto? Permission { get; set; }
    }

    public class UserPermissionDto
    {
        public int UserID { get; set; }
        public int CompanyID { get; set; }
        public string? Department { get; set; }
        public string UserRole { get; set; } = string.Empty;
        public string RoleKey { get; set; } = string.Empty;
        public string Tier { get; set; } = string.Empty;
        public bool Page1Access { get; set; }
        public bool Page2Access { get; set; }
        public bool Page3Access { get; set; }
        public bool Page4Access { get; set; }
        public string DataScope { get; set; } = string.Empty;
        public bool CanViewVendor { get; set; }
        public bool CanViewCost { get; set; }
        public bool CanViewCustomer { get; set; }
    }

    public class LoginResult
    {
        public string Token { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
    }

    public class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class UserCompanyDto
    {
        public int CompanyID { get; set; }
        public string CompanyCode { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
    }

    public class RefreshToken
    {
        public int RefreshTokenID { get; set; }
        public int UserID { get; set; }
        public string Token { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
        public bool IsRevoked { get; set; }

        public MsUser User { get; set; } = null!;
    }

    public class SwitchCompanyRequest
    {
        public int CompanyID { get; set; }
    }

    public class RefreshResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public int? CurrentCompanyId { get; set; }
    }

    public class SwitchCompanyResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public int CurrentCompanyId { get; set; }
        public UserPermissionDto? Permission { get; set; }
    }
}