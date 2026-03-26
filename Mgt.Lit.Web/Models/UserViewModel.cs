namespace Mgt.Lit.WebFront.Models
{
    public class UserViewModel
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string UserRole { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;
    }
}
