using System.ComponentModel.DataAnnotations;

namespace RenewLedger.Pages;

public sealed class SubscriptionInput
{
    public Guid? Id { get; set; }
    public Guid Revision { get; set; }

    [Required(ErrorMessage = "请输入项目名称。")]
    [StringLength(100, ErrorMessage = "项目名称不能超过 100 个字符。")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "请输入金额。")]
    [Range(typeof(decimal), "0", "1000000000000", ErrorMessage = "金额必须在 0 到 1000000000000 之间。")]
    public decimal? Amount { get; set; }

    public string Currency { get; set; } = "CNY";
    public string Cycle { get; set; } = "monthly";

    [Required(ErrorMessage = "请选择到期日。")]
    public DateOnly? Due { get; set; }

    [Range(1, 31, ErrorMessage = "续费日必须在 1 到 31 之间。")]
    public int? AnchorDay { get; set; }
    public bool EndOfMonth { get; set; }
    public bool IsActive { get; set; } = true;
}
