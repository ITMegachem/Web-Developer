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
            // Find by username only — the password is checked in code (hash first, plaintext fallback).
            var user = await _context.MsUsers.FirstOrDefaultAsync(u =>
                u.Username == username &&
                u.IsActive);
            if (user == null) return null;

            if (!PasswordService.Verify(user, password, out var hashChanged))
                return null;

            if (hashChanged)
                await _context.SaveChangesAsync();   // store the new PasswordHash (self-heal)

            return user;
        }
    }
}
