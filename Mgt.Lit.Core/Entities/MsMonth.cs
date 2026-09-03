using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mgt.Lit.Core.Entities
{
    [Table("Ms_Month")]
    public class MsMonth
    {
        [Key]
        public int MonthId { get; set; }
        public string MonthNameEN { get; set; } = string.Empty;
    }
}