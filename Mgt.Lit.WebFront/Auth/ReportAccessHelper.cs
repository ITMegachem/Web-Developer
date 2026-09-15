using System.Security.Claims;

namespace Mgt.Lit.WebFront.Auth
{
    // ★ ตรรกะสิทธิ์ของ Sales Intelligence Matrix (9 รายงาน) — mirror ของ DashboardController.cs ฝั่ง WebApi
    // (CanAccessReport/IsFullAccessUser/IsCsrRestricted) ใช้สำหรับซ่อนเมนู/ปุ่ม Export ฝั่ง client เท่านั้น
    // การบังคับใช้จริง (กันข้ามสิทธิ์ผ่าน URL ตรงๆ) อยู่ที่ backend เสมอ — ถ้าแก้กติกาต้องแก้ทั้งสองที่ให้ตรงกัน
    public static class ReportAccessHelper
    {
        private static readonly HashSet<string> FullAccessUsernames = new(StringComparer.OrdinalIgnoreCase)
        { "MGT000298", "MGT000297", "MGT000018" };

        // ★ Block list เฉพาะบุคคล/กลุ่ม — ห้ามเห็น Sales Intelligence Matrix ทั้งหมด ไม่ว่า Department/UserRole จะเป็นอะไร
        // ต้องเช็คก่อน IsFullAccessUser เสมอ (แม้จะเป็น username พิเศษ/Manager/Admin/IT ก็ยังถูกบล็อก)
        private static readonly HashSet<string> BlockedFromMatrixUsernames = new(StringComparer.OrdinalIgnoreCase)
        { "MGT000185", "MGT000142" };

        // ★ MGT000142 ถูกบล็อกเพิ่มจาก Dashboard (DB1-4) ด้วย ไม่ใช่แค่ Sales Intelligence Matrix เฉยๆ
        // เก็บแยกจาก BlockedFromMatrixUsernames เพราะ MGT000185/GLC* ยังไม่ได้ถูกขอให้บล็อก Dashboard
        private static readonly HashSet<string> BlockedFromDashboardUsernames = new(StringComparer.OrdinalIgnoreCase)
        { "MGT000142" };

        private static readonly HashSet<string> CsrAllowedReports = new(StringComparer.OrdinalIgnoreCase)
        { "SalesForecastAccuracy", "ForecastMonthly" };

        private static string Username(ClaimsPrincipal? user) => user?.Identity?.Name ?? "";
        private static string Department(ClaimsPrincipal? user) => (user?.FindFirst("Department")?.Value ?? "").Trim().ToUpperInvariant();
        private static string Position(ClaimsPrincipal? user) => (user?.FindFirst("Position")?.Value ?? "").Trim().ToUpperInvariant();

        // ★ เผื่อ token บางเคส (เช่นผ่าน SSO) ไม่ map short claim "role" กลับเป็น ClaimTypes.Role ให้อัตโนมัติ
        // normalize เป็นตัวพิมพ์ใหญ่ด้วยเพื่อกัน casing ไม่ตรงกัน (DB เก็บ "Manager"/"manager" ปนกันได้)
        private static string UserRole(ClaimsPrincipal? user) =>
            (user?.FindFirst(ClaimTypes.Role)?.Value
                ?? user?.FindFirst("userRole")?.Value
                ?? user?.FindFirst("role")?.Value
                ?? "").Trim().ToUpperInvariant();

        private static bool IsSpecialFullAccessUser(ClaimsPrincipal? user) =>
            FullAccessUsernames.Contains(Username(user));

        // Username ขึ้นต้นด้วย "GLC" (เช่น user บริษัทในเครือ/บัญชีทดสอบ) -> ห้ามเห็น Sales Intelligence Matrix เลย
        private static bool IsBlockedFromMatrix(ClaimsPrincipal? user) =>
            BlockedFromMatrixUsernames.Contains(Username(user))
            || Username(user).StartsWith("GLC", StringComparison.OrdinalIgnoreCase);

        public static bool IsBlockedFromDashboard(ClaimsPrincipal? user) =>
            BlockedFromDashboardUsernames.Contains(Username(user));

        private static bool IsCsrRestricted(ClaimsPrincipal? user) =>
            !IsSpecialFullAccessUser(user) && Department(user) == "CSR";

        private static bool IsEligibleForMatrix(ClaimsPrincipal? user) =>
            IsSpecialFullAccessUser(user) || Department(user) is "SALES" or "CSR" or "IT";

        public static bool IsFullAccessUser(ClaimsPrincipal? user) =>
            IsSpecialFullAccessUser(user)
            || Position(user) is "CMO" or "CFO" or "IT"
            || Department(user) == "IT"
            || UserRole(user) is "MANAGER" or "ADMIN";

        // true = แสดงเมนู/โหลดรายงานนี้ได้
        // ★ เช็ค IsFullAccessUser ก่อนเสมอ — Manager/Admin/IT ไม่ผูกกับ Department ใน IsEligibleForMatrix เลย
        // ต้องข้ามด่านนั้นไปได้เต็มๆ ไม่งั้น Manager/Admin ที่ Department ไม่ใช่ Sales/CSR/IT จะโดนซ่อนเมนูทุกรายงาน
        public static bool CanAccessReport(ClaimsPrincipal? user, string reportKey)
        {
            if (IsBlockedFromMatrix(user)) return false;
            if (IsFullAccessUser(user)) return true;
            if (!IsEligibleForMatrix(user)) return false;
            if (IsCsrRestricted(user)) return CsrAllowedReports.Contains(reportKey);
            return true;
        }

        // true = แสดงปุ่ม Export Excel ของรายงานนี้
        public static bool CanExportReport(ClaimsPrincipal? user, string reportKey)
        {
            if (!CanAccessReport(user, reportKey)) return false;
            if (IsFullAccessUser(user)) return true;

            var role = UserRole(user);
            if (role == "LEADER" && Department(user) == "SALES") return true;

            // พนักงานขายทั่วไป export ได้เฉพาะ Visit Report
            return string.Equals(reportKey, "VisitDailyReport", StringComparison.OrdinalIgnoreCase);
        }
    }
}
