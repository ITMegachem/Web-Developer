using Mgt.Lit.Core.Entities;
using Microsoft.AspNetCore.Identity;

namespace Mgt.Lit.Core.Services
{
    // Password hashing for Ms_User — SAME format as the OCR system (ASP.NET Core Identity
    // PasswordHasher, PBKDF2 V3), because both systems share Ms_User in MGT_Datawarehouse.
    // A hash written by Dashboard works in OCR and vice versa.
    //
    // Transition period:
    //   PasswordHash present → verify against the hash (normal path)
    //   no hash / hash doesn't match → compare with the old plaintext Password
    //       match   → login OK + write PasswordHash (self-heal; next time uses the hash)
    //       no match → fail
    // When plaintext is retired: set AllowPlaintextFallback = false (then drop the column).
    public static class PasswordService
    {
        public static bool AllowPlaintextFallback { get; set; } = true;

        private static readonly PasswordHasher<MsUser> _hasher = new();

        public static string Hash(MsUser user, string password) => _hasher.HashPassword(user, password);

        /// Returns true when the password is correct. Sets <paramref name="hashChanged"/> when
        /// user.PasswordHash was (re)written and needs SaveChanges.
        public static bool Verify(MsUser user, string password, out bool hashChanged)
        {
            hashChanged = false;
            if (string.IsNullOrEmpty(password)) return false;

            if (!string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                PasswordVerificationResult r;
                try { r = _hasher.VerifyHashedPassword(user, user.PasswordHash, password); }
                catch (FormatException) { r = PasswordVerificationResult.Failed; }   // corrupted hash

                if (r == PasswordVerificationResult.Success) return true;
                if (r == PasswordVerificationResult.SuccessRehashNeeded)
                {
                    user.PasswordHash = Hash(user, password);
                    hashChanged = true;
                    return true;
                }
            }

            // Fallback: old plaintext column (also covers a plaintext edited by hand in the DB).
            if (AllowPlaintextFallback && !string.IsNullOrEmpty(user.Password) && user.Password == password)
            {
                user.PasswordHash = Hash(user, password);
                hashChanged = true;
                return true;
            }
            return false;
        }
    }
}
