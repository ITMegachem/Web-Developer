namespace Mgt.Lit.WebFront.Models.Dashboard
{
    public record DashboardNavItem(
     string Href,
     string LabelKey,
     string Icon,
     Func<bool> CanView,
     // คลาส css เสริมต่อรายการ เช่น "link-compact" = ตัวอักษรเล็กเท่าเมนูย่อย
     string? ExtraClass = null);
}
