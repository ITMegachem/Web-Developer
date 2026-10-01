namespace Mgt.Lit.Core.DTOs.Admin
{
    public class AdminUserListItemDto
    {
        public int UserID { get; set; }
        public string? Username { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? UserRole { get; set; }
        public string? Division { get; set; }
        public bool IsActive { get; set; }
        public bool HasPasswordHash { get; set; }
        public string? PrimaryCompanyCode { get; set; }
        public List<string> CompanyCodes { get; set; } = new();
    }

    public class AdminCreateUserDto
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string UserRole { get; set; } = "user";
        public string? Division { get; set; }
        public bool IsActive { get; set; } = true;
        public List<int> CompanyIDs { get; set; } = new();
        public int? PrimaryCompanyID { get; set; }
    }

    public class AdminResetPasswordDto
    {
        public string NewPassword { get; set; } = "";
    }

    public class AdminUserLookupsDto
    {
        public List<AdminCompanyItemDto> Companies { get; set; } = new();
        public List<string> Roles { get; set; } = new();
        public List<string> Divisions { get; set; } = new();
    }

    public class AdminCompanyItemDto
    {
        public int CompanyID { get; set; }
        public string CompanyCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
    }
}
