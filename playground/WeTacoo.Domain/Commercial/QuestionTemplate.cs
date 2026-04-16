namespace WeTacoo.Domain.Commercial;
using WeTacoo.Domain.Common;

public class QuestionTemplate : AggregateRoot
{
    public QuestionTemplate() { Id = NextId("qtpl"); }
    public string Question { get; set; } = "";
    public string QuestionType { get; set; } = "text"; // text, number, boolean, select
    public string? Visibility { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}
