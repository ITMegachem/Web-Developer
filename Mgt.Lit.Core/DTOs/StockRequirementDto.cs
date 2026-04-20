using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Mgt.Lit.Core.DTOs
{
    public class StockRequirementRequest
    {
        public string Material_Code { get; set; }
        public string Plant { get; set; }
        public List<object> _Detail { get; set; } = new List<object>();
    }

    // ปรับแต่งฟิลด์ตาม JSON Response ที่ได้จาก SAP
    public class StockRequirementResponse
    {
        [JsonPropertyName("material_Code")]
        public string Material_Code { get; set; }

        [JsonPropertyName("plant")]
        public string Plant { get; set; }

        [JsonPropertyName("_Detail")]
        public List<StockDetailDto> _Detail { get; set; }
    }

    public class StockDetailDto
    {
        public DateTime Document_Date { get; set; }
        public string Element_Type { get; set; }
        public string Document_No { get; set; }
        public string Ref_Doc { get; set; }
        public string Material_Code { get; set; }
        public string Plant { get; set; }
        public decimal Receipt_Reqmt_Qty { get; set; }
        public string Unit { get; set; }
        public decimal available_qty { get; set; }
        public string Additional_Info_In { get; set; }
        public string Additional_Info_Out { get; set; }  // ✅ ใช้ชื่อนี้
        public string Vendor_Code { get; set; }
        public string Vendor_Name { get; set; }
        public string Customer_Code { get; set; }        // ✅ ใช้ชื่อนี้
        public string Customer_Name { get; set; }        // ✅ ใช้ชื่อนี้
        public int Priority { get; set; }

        public string? MaterialGroup { get; set; }
        public string? MaterialGroupDescription { get; set; }
        public string? SalesGroup { get; set; }
        [JsonPropertyName("industryCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)] // ✅ บังคับออกเสมอ
        public string? IndustryCode { get; set; }

        [JsonPropertyName("industryName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)] // ✅ บังคับออกเสมอ
        public string? IndustryName { get; set; }
    }
    public class PlantDto
    {
        public string PlantCode { get; set; }
        public string PlantName { get; set; }
    }
    public class StockMovementRequestDto
    {
        public string? Material { get; set; }
        public string? Plant { get; set; }
    }
    public class SapWarehouseStockResponseDto
    {
        public string Material_Code { get; set; }
        public string Batch_Code { get; set; }

        public List<SapWarehouseStockDetailDto> _Detail { get; set; } = new();
    }

    public class SapWarehouseStockDetailDto
    {
        public string Material_Code { get; set; }
        public string Material_Name { get; set; }

        public string Material_Group { get; set; }
        public string Material_Group_Name { get; set; }
        public string Plant { get; set; }
        public string Storage_Loc { get; set; }

        public string Batch_Code { get; set; }

        public DateTime? EXP_Date { get; set; }

        public decimal Unrestricted_Stock { get; set; }

        public decimal Blocked_Stock { get; set; }

        public decimal Stock_in_QI { get; set; }

        public string Unit { get; set; }

        public decimal Value_of_Unrestricted_Stock { get; set; }

        public decimal Value_of_Blocked_Stock { get; set; }

        public decimal Value_of_Stock_in_QI { get; set; }

        public decimal Price_KG { get; set; }
    }
}
