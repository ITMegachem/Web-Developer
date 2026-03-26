using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.Entities;
using Mgt.Lit.Core.Interfaces;
using Microsoft.EntityFrameworkCore;


namespace Mgt.Lit.Core.Services
{
    public class UserService : IUserRepository
    {
        private readonly AppDbContext _context;

        public UserService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<MsUser?> LoginAsync(string username, string password)
        {
            return await _context.MsUsers.FirstOrDefaultAsync(u =>
                u.Username == username &&
                u.Password == password &&
                u.IsActive);
        }
    }
}
