namespace Mgt.Lit.WebFront.Models.Dashboard
{
    public record DashboardNavItem(
     string Href,
     string LabelKey,
     string Icon,
     Func<bool> CanView);
}
