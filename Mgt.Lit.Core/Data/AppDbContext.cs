using Mgt.Lit.Core.Data;
using Mgt.Lit.Core.DTOs;
using Mgt.Lit.Core.DTOs.Admin;
using Mgt.Lit.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mgt.Lit.Core.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }
        public DbSet<DownloadLog> DownloadLogs { get; set; }
        public DbSet<MsUser> MsUsers { get; set; }
        public DbSet<MsCompany> MsCompanies { get; set; }
        public DbSet<MsUserCompany> MsUserCompanies { get; set; }

        // 📊 View สำหรับรายงานการขาย
        public DbSet<View_MGT_GLC_ALL_Sales> View_MGT_GLC_ALL_Sales { get; set; }
        public DbSet<View_MaterialStock_WeightKG> View_MaterialStock_WeightKG { get; set; }
        public DbSet<Ms_BusinessPartnerCustSalesPartnerFunc> Ms_BusinessPartnerCustSalesPartnerFunc { get; set; }
        public DbSet<View_UserPermission> View_UserPermissions { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<View_UserPermission>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("View_UserPermission");
            });
            modelBuilder.Entity<Ms_BusinessPartnerCustSalesPartnerFunc>()
    .ToTable("Ms_BusinessPartnerCustSalesPartnerFunc")
    .HasKey(x => x.Id);
            // 🔍 1. จัดการ View (เนื่องจากไม่มี Primary Key และต้องการ Query อย่างเดียว)
            modelBuilder.Entity<View_MGT_GLC_ALL_Sales>(entity =>
            {
                entity.HasNoKey(); // บอก EF ว่า View นี้ไม่มี Key
                entity.ToView("View_MGT_GLC_ALL_Sales"); // ระบุให้ชัดว่าเป็น View ไม่ใช่ Table
                entity.Property(e => e.SalesOrganization).HasColumnType("varchar(4)");
                // (Optional) กำหนดประเภทข้อมูลสำหรับยอดเงิน เพื่อความแม่นยำตอน Query
                entity.Property(e => e.NetAmount).HasColumnType("decimal(18, 5)");
                entity.Property(e => e.CostAmount).HasColumnType("decimal(18, 5)");
                entity.Property(e => e.GrossProfit).HasColumnType("decimal(18, 5)");
                entity.Property(e => e.Quantity).HasColumnType("decimal(18, 5)");
            });
            // ✨ แก้ไขส่วนจัดการ View Stock ให้รองรับตัวเลขขนาดใหญ่ขึ้นเพื่อแก้ปัญหา Overflow
            modelBuilder.Entity<View_MaterialStock_WeightKG>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("View_MaterialStock_WeightKG");

                // 💡 ปรับจาก (18, 3) เป็น (38, 6) เพื่อให้ตรงกับ SQL View ที่เราแก้ใหม่
                // การขยายตรงนี้จะช่วยให้เวลาค้นหา Sales Org 2000 ที่มีข้อมูลเยอะๆ แล้วคำนวณน้ำหนัก ไม่หลุด Overflow ครับ
                entity.Property(e => e.Qty_BaseUnit).HasColumnType("decimal(38, 6)");
                entity.Property(e => e.TotalWeightKG).HasColumnType("decimal(38, 6)");
                entity.Property(e => e.NetWeight).HasColumnType("decimal(38, 6)");

                // 🚀 เพิ่มประสิทธิภาพการค้นหา SalesOrganization (ถ้าใน View มีฟิลด์นี้)
                // การระบุประเภทข้อมูลที่ชัดเจนจะช่วยให้ Index ใน SQL ทำงานได้เร็วขึ้น
                // entity.Property(e => e.SalesOrganization).HasColumnType("char(4)"); 
            });
            modelBuilder.Entity<View_Sales_with_Op_SalesOrder>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("View_Sales_with_Op_SalesOrder");
            });
            // 🔑 2. จัดการ Composite Key สำหรับ MsUserCompany
            modelBuilder.Entity<MsUserCompany>()
                .HasKey(x => new { x.UserID, x.CompanyID });
            modelBuilder.Entity<DownloadLog>().ToTable("DownloadLogs");
            // 🏗️ 3. Mapping ชื่อตารางให้ตรงกับ SQL Server
            modelBuilder.Entity<MsCompany>().ToTable("Ms_Company");
            modelBuilder.Entity<MsUserCompany>().ToTable("Ms_UserCompany");
            modelBuilder.Entity<MsUser>().ToTable("Ms_User");
            modelBuilder.Entity<ActivityLog>().ToTable("ActivityLog");
            modelBuilder.Entity<ErrorLog>().ToTable("ErrorLog");
            modelBuilder.Entity<Entities.RefreshToken>().ToTable("RefreshTokens", "dbo");
            modelBuilder.Entity<Ms_MaterialGroup1>().HasNoKey();
            // OnModelCreating
            modelBuilder.Entity<Mapping_Soldto>().HasNoKey().ToTable("Mapping_Soldto");
            modelBuilder.Entity<Mapping_Shipto>().HasNoKey().ToTable("Mapping_Shipto");
            modelBuilder.Entity<Op_SalesOrder>(entity =>
            {
                entity.HasKey(x => x.SalesOrder);
                entity.ToTable("Op_SalesOrder");
            });
            modelBuilder.Entity<Ms_CustomerGroup>(entity =>
            {
                entity.HasKey(x => new { x.SOrg, x.CustomerGroup });
                entity.ToTable("Ms_CustomerGroup");
            });
            modelBuilder.Entity<Ms_ProductDescription>(entity =>
            {
                entity.HasNoKey();
                entity.ToTable("Ms_ProductDescription");
            });
        }

        public DbSet<Entities.RefreshToken> RefreshTokens { get; set; }
        public DbSet<ActivityLog> ActivityLogs { get; set; }
        public DbSet<ErrorLog> ErrorLogs { get; set; }

        public DbSet<Ms_Product> Ms_Product { get; set; }
        public DbSet<Ms_MaterialGroup1> Ms_MaterialGroup1 { get; set; }
        public DbSet<View_Sales_with_Op_SalesOrder> View_Sales_with_Op_SalesOrder { get; set; }
        public DbSet<Mapping_Soldto> Mapping_Soldtos { get; set; }
        public DbSet<Mapping_Shipto> Mapping_Shiptos { get; set; }
        public DbSet<Op_SalesOrder> Op_SalesOrders { get; set; }
        public DbSet<Ms_CustomerGroup> Ms_CustomerGroups { get; set; }
        public DbSet<Ms_ProductDescription> Ms_ProductDescription { get; set; }
        
    }
}