namespace Mgt.Lit.WebFront.Models.Member
{
    public class UserViewModel
    {
        public int UserId { get; set; }

        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string UserRole { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;

        // ✅ เพิ่มใหม่
        public int? PrimaryCompanyID { get; set; }
        public string? PrimaryCompanyCode { get; set; }

        // Admin = หลายบริษัท / User = 1 บริษัท
        public List<UserCompanyViewModel> Companies { get; set; } = new();
    }

    public class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; } = "";
        public string NewPassword { get; set; } = "";
    }
    public class UserCompanyViewModel
    {
        public int CompanyID { get; set; }
        public string CompanyCode { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
    }
}
