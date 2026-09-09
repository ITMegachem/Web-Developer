using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Mgt.Lit.Core.Data;

namespace Mgt.Lit.Core.Services.Dashboard
{
    public interface ISalesEmployeeNameResolver
    {
        Task<Dictionary<string, string>> BuildNameMapAsync(IEnumerable<string?> rawNames, CancellationToken ct = default);

        // เฉพาะรายงานที่ล็อกด้วย BU (SalesGroup) — คัดชื่อที่ Division ประจำของ user (Ms_User.Division) ตรงกับ BU ที่ล็อก
        // เท่านั้น เพราะ MGT_Sale.SalesEmployeeBP บางแถวเป็น "รายการที่หลุด" ข้าม BU ของพนักงานคนนั้นได้ (เช่น มี 1 บิลจาก
        // พนักงาน BU4 ไปโผล่ใน SalesGroup=BU2) การกรองแค่ระดับแถวธุรกรรมจึงทำให้ dropdown โชว์คนที่ไม่ใช่ทีมจริงของ BU นั้น
        Task<List<string>> FilterToDivisionAsync(List<string> fullNames, string? division, CancellationToken ct = default);

        // เหมือน FilterToDivisionAsync แต่คืนเป็น HashSet ไว้กรองแถวข้อมูลดิบ (raw rows) ก่อน group/sum เช่นตาราง
        // "Total Revenue by Salesman" — คืน null เมื่อ division ว่าง (แปลว่า "ไม่ล็อก BU ไม่ต้องกรอง" ใช้ทุกแถวตามเดิม)
        Task<HashSet<string>?> GetHomeDivisionNameSetAsync(string? division, CancellationToken ct = default);

        // ★ นิยาม "ข้อมูลของ BU นี้" ใหม่ทั้งระบบ (Dashboard + Sales Intelligence Matrix ทั้ง 9 รายงาน) — อ้างอิงจาก
        // Ms_User.Division + Department='Sales' (พนักงานขายที่สังกัด BU นั้นจริง) แทนการเชื่อ MGT_Sale/MGT_Deal.SalesGroup
        // ของธุรกรรมเอง (ซึ่งพบว่าบางธุรกรรมติด SalesGroup ผิด BU ของคนขายจริง) — คืนชื่อเต็ม (Ms_User.FullName)
        // ★★★ ไม่กรอง IsActive โดยตั้งใจ — ยืนยันแล้วว่าพนักงานที่ลาออก/ถูกปิดบัญชีไปแล้วยังมียอดขายจริงในอดีตค้างอยู่
        // (พบ 594M ที่เคยหายไปจาก BU3 ตอนกรอง IsActive) ยอดขายเก่าต้องนับเข้า BU เดิมของเขาเสมอไม่ว่าจะยัง Active หรือไม่
        // ชื่อ method ยังใช้คำว่า "Active" ตามเดิม (ไม่ใช่ความหมายตรงตัวอีกต่อไป) เพื่อลดการแก้ไขจุดเรียกใช้ทั้ง 16 ไฟล์
        // สำหรับใช้กรอง MGT_Sale.SalesEmployeeBP แบบ exact match ตรงๆ (SAP เก็บชื่อเต็มอยู่แล้ว)
        Task<HashSet<string>> GetActiveSalesEmployeeFullNamesAsync(string division, CancellationToken ct = default);

        // เหมือนกันแต่คืนเป็น "ชื่อดิบ" ที่เจอจริงใน MGT_Deal.SalesEmployeeBP (ชื่อบางส่วนจาก Zoho) ของพนักงานกลุ่มนี้
        // ไว้กรอง MGT_Deal.SalesEmployeeBP แบบ exact match กับชื่อดิบได้ตรงๆ (WHERE SalesEmployeeBP IN (...))
        Task<List<string>> GetActiveSalesEmployeeRawDealNamesAsync(string division, CancellationToken ct = default);

        // เหมือนกันแต่คืนเป็น "ชื่อดิบ" ที่เจอจริงใน MGT_VisitReport.SalesEmployeeBP (คนละ universe ของชื่อดิบกับ MGT_Deal
        // แม้จะเป็นพนักงานคนเดียวกัน เพราะ Zoho เก็บชื่อบางส่วนไม่คงที่ระหว่าง module) ไว้กรอง Visit Report แบบเดียวกัน
        Task<List<string>> GetActiveSalesEmployeeRawVisitNamesAsync(string division, CancellationToken ct = default);

        // Ms_User.FullName -> Division ของพนักงานขาย (Department='Sales') ทุกคนทุก BU รวดเดียว (ไม่กรอง IsActive เช่นกัน
        // ดู comment ที่ GetActiveSalesEmployeeFullNamesAsync) — ใช้จัดประเภทแต่ละแถวธุรกรรมว่า "นับเป็นของ BU ไหน"
        // (เช่น Total BU1-4 breakdown) แทนการเชื่อ SalesGroup ของธุรกรรมเอง
        Task<Dictionary<string, string>> GetActiveSalesEmployeeDivisionMapAsync(CancellationToken ct = default);
    }

    // Zoho เก็บชื่อ Salesperson (SalesEmployeeBP) เป็นชื่อบางส่วนจาก Owner.name เท่านั้น (เช่น "Dooduang" แทนที่จะเป็น
    // "Pattamawan Dooduang" แบบใน Ms_User.FullName) เนื่องจากไม่มี join key จริงระหว่าง Zoho user กับ Ms_User
    // จึง match แบบ substring แทน (ยอมรับความเสี่ยงเรื่องชื่อซ้ำ/คล้ายกันเป็น trade-off) — ใช้ร่วมกันทุกรายงานที่แสดง Salesperson
    public class SalesEmployeeNameResolver : ISalesEmployeeNameResolver
    {
        private readonly AppDbContext _db;

        public SalesEmployeeNameResolver(AppDbContext db) => _db = db;

        // ★ คำที่สะกดผิดใน Zoho user profile (ยืนยันแล้วว่าเป็นปัญหาที่ตัว Zoho เอง ไม่ใช่ปัญหาโค้ด แก้จาก Zoho โดยตรงไม่ได้)
        // ทำเป็น substring replace แทน exact-match dictionary เพราะ Owner field ที่ COQL/REST API คืนมาบางครั้งเป็นชื่อเต็ม
        // ("Jirapon Laipuenpthong") บางครั้งเป็นแค่นามสกุล ("Laipuenpthong") แล้วแต่ endpoint/รอบ sync — substring replace
        // จับได้ทั้งสองแบบ โดยไม่ต้องแจกแจงทุก combination ล่วงหน้า — แก้แบบเจาะจงทีละรายที่ยืนยันแล้วแทนการทำ fuzzy matching
        // ทั่วระบบ (เสี่ยงจับคู่ผิดคนกับชื่อที่คล้ายกันโดยไม่ได้ตั้งใจ) — เพิ่มรายการใหม่ที่นี่เมื่อเจอเคสเพิ่มเติม
        private static readonly (string Find, string Replace)[] KnownRawNameCorrections =
        {
            ("Laipuenpthong", "Laipuengthong"),   // Jirapon Laipuengthong
        };

        private static string NormalizeRawName(string raw)
        {
            foreach (var (find, replace) in KnownRawNameCorrections)
            {
                var idx = raw.IndexOf(find, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    return raw.Remove(idx, find.Length).Insert(idx, replace);
            }
            return raw;
        }

        public async Task<Dictionary<string, string>> BuildNameMapAsync(IEnumerable<string?> rawNames, CancellationToken ct = default)
        {
            var distinctRaw = rawNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).Distinct().ToList();
            var map = new Dictionary<string, string>();
            if (distinctRaw.Count == 0) return map;

            var fullNames = await _db.MsUsers.AsNoTracking()
                .Where(u => u.IsActive && u.FullName != null && u.FullName != "")
                .Select(u => u.FullName!)
                .ToListAsync(ct);

            foreach (var raw in distinctRaw)
            {
                var match = fullNames.FirstOrDefault(fn => fn.Contains(NormalizeRawName(raw), StringComparison.OrdinalIgnoreCase));
                map[raw] = match ?? raw;
            }
            return map;
        }

        // ★ เฉพาะ dropdown/ตัวเลือกที่ให้ผู้ใช้เลือกเท่านั้น — คงเงื่อนไข IsActive ไว้ (ไม่ให้พนักงานที่ลาออกไปแล้วโผล่เป็น
        // ตัวเลือกใหม่) ต่างจาก GetActiveSalesEmployeeFullNamesAsync ที่ใช้จัดประเภท "ยอดขายที่เกิดขึ้นแล้วนับเป็นของ BU ไหน"
        // ซึ่งต้องนับพนักงานที่ลาออกไปแล้วด้วย เพราะยอดขายในอดีตของเขาเป็นข้อมูลจริงที่เกิดขึ้นตอนยังทำงานอยู่
        public async Task<List<string>> FilterToDivisionAsync(List<string> fullNames, string? division, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(division) || fullNames.Count == 0) return fullNames;

            var homeDivisionNames = await _db.MsUsers.AsNoTracking()
                .Where(u => u.IsActive && u.Department == "Sales" && u.Division == division && u.FullName != null && fullNames.Contains(u.FullName))
                .Select(u => u.FullName!)
                .ToListAsync(ct);

            return fullNames.Where(n => homeDivisionNames.Contains(n)).ToList();
        }

        public async Task<HashSet<string>?> GetHomeDivisionNameSetAsync(string? division, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(division)) return null;

            var names = await _db.MsUsers.AsNoTracking()
                .Where(u => u.IsActive && u.Department == "Sales" && u.Division == division && u.FullName != null && u.FullName != "")
                .Select(u => u.FullName!)
                .ToListAsync(ct);

            return names.ToHashSet();
        }

        // ★ นิยาม "ข้อมูลของ BU นี้" สำหรับจัดประเภทยอดขาย — ไม่กรอง IsActive โดยตั้งใจ เพราะพนักงานที่ลาออก/ถูกปิดบัญชี
        // ไปแล้ว ยอดขายในอดีตของเขา (ตอนยังทำงานอยู่) ยังเป็นข้อมูลจริงที่ต้องนับเข้า BU เดิมของเขาเสมอ (ยืนยันแล้วว่าตัด
        // IsActive ออกทำให้ยอดขายจริงหลายร้อยล้านหายไปจาก BU3 อย่างผิดๆ) เงื่อนไขเดียวที่เหลือคือ Division + Department='Sales'
        public async Task<HashSet<string>> GetActiveSalesEmployeeFullNamesAsync(string division, CancellationToken ct = default)
        {
            var names = await _db.MsUsers.AsNoTracking()
                .Where(u => u.Department == "Sales" && u.Division == division && u.FullName != null && u.FullName != "")
                .Select(u => u.FullName!)
                .ToListAsync(ct);

            return names.ToHashSet();
        }

        public async Task<List<string>> GetActiveSalesEmployeeRawDealNamesAsync(string division, CancellationToken ct = default)
        {
            var activeFullNames = await GetActiveSalesEmployeeFullNamesAsync(division, ct);
            if (activeFullNames.Count == 0) return new List<string>();

            var rawDealNames = await _db.MGT_Deal.AsNoTracking()
                .Where(d => d.SalesEmployeeBP != null && d.SalesEmployeeBP != "")
                .Select(d => d.SalesEmployeeBP!)
                .Distinct()
                .ToListAsync(ct);

            return rawDealNames
                .Where(raw => activeFullNames.Any(full => full.Contains(NormalizeRawName(raw), StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        public async Task<List<string>> GetActiveSalesEmployeeRawVisitNamesAsync(string division, CancellationToken ct = default)
        {
            var activeFullNames = await GetActiveSalesEmployeeFullNamesAsync(division, ct);
            if (activeFullNames.Count == 0) return new List<string>();

            var rawVisitNames = await _db.MGT_VisitReport.AsNoTracking()
                .Where(v => v.SalesEmployeeBP != null && v.SalesEmployeeBP != "")
                .Select(v => v.SalesEmployeeBP!)
                .Distinct()
                .ToListAsync(ct);

            return rawVisitNames
                .Where(raw => activeFullNames.Any(full => full.Contains(NormalizeRawName(raw), StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        // ไม่กรอง IsActive (ดู comment ที่ GetActiveSalesEmployeeFullNamesAsync) — ยอดขายในอดีตของพนักงานที่ลาออกไปแล้ว
        // ยังต้องจัดเข้า BU เดิมของเขาเสมอ
        public async Task<Dictionary<string, string>> GetActiveSalesEmployeeDivisionMapAsync(CancellationToken ct = default)
        {
            var rows = await _db.MsUsers.AsNoTracking()
                .Where(u => u.Department == "Sales" && u.FullName != null && u.FullName != "" && u.Division != null && u.Division != "")
                .Select(u => new { u.FullName, u.Division })
                .ToListAsync(ct);

            // ★ กันชื่อซ้ำ (ไม่ควรเกิดแต่กันไว้) — ใช้ค่าแรกที่เจอ ไม่ throw
            var map = new Dictionary<string, string>();
            foreach (var r in rows)
                map.TryAdd(r.FullName!, r.Division!);
            return map;
        }
    }
}
