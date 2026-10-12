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
        public string? Tier { get; set; }
        public string? Department { get; set; }
        public string? Position { get; set; }
        public bool IsActive { get; set; }
        public bool HasPasswordHash { get; set; }
        public string? PrimaryCompanyCode { get; set; }
        public List<string> CompanyCodes { get; set; } = new();
        public List<int> CompanyIDs { get; set; } = new();
        public int? PrimaryCompanyID { get; set; }
    }

    public class AdminCreateUserDto
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string UserRole { get; set; } = "user";
        public string? Division { get; set; }
        public string? Tier { get; set; }
        public string? Department { get; set; }
        public string? Position { get; set; }
        public bool IsActive { get; set; } = true;
        public List<int> CompanyIDs { get; set; } = new();
        public int? PrimaryCompanyID { get; set; }
    }

    public class AdminUpdateUserDto
    {
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string UserRole { get; set; } = "user";
        public string? Division { get; set; }
        public string? Tier { get; set; }
        public string? Department { get; set; }
        public string? Position { get; set; }
        public bool IsActive { get; set; } = true;
        public List<int> CompanyIDs { get; set; } = new();
        public int? PrimaryCompanyID { get; set; }
        // false = leave Ms_UserPermissionOverride untouched.
        // true + Override null = delete the row (back to role defaults); true + Override = insert/update.
        public bool UpdateOverride { get; set; }
        public AdminPermissionOverrideDto? Override { get; set; }
    }

    public class AdminPermissionOverrideDto
    {
        public int? CompanyID { get; set; }      // read-only for the UI: always = primary company
        public string DataScope { get; set; } = "OWN";
        public bool Page1Access { get; set; }
        public bool Page2Access { get; set; }
        public bool Page3Access { get; set; }
        public bool Page4Access { get; set; }
        public bool CanViewVendor { get; set; }
        public bool CanViewCost { get; set; }
        public bool CanViewCustomer { get; set; }
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
        public List<string> Tiers { get; set; } = new();
        public List<string> Departments { get; set; } = new();
        public List<string> Positions { get; set; } = new();
    }

    public class AdminCompanyItemDto
    {
        public int CompanyID { get; set; }
        public string CompanyCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
    }
}
