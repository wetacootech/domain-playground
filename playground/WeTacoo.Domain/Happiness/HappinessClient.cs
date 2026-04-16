namespace WeTacoo.Domain.Happiness;
using WeTacoo.Domain.Common;

public class HappinessService : Entity
{
    public HappinessService() { Id = NextEntityId("hsvc"); }
    public string ServiceId { get; set; } = "";
    public int? SatisfactionScore { get; set; }
    public string? Feedback { get; set; }
    public DateTime? SurveyDate { get; set; }
}

public class HappinessClient : AggregateRoot
{
    public HappinessClient() { Id = NextId("happy"); }
    public string CommercialLeadId { get; set; } = "";
    public List<HappinessService> Services { get; set; } = [];
    public double AverageScore => Services.Where(s => s.SatisfactionScore.HasValue).Select(s => (double)s.SatisfactionScore!.Value).DefaultIfEmpty(0).Average();

    public void RecordSatisfaction(string serviceId, int score, string? feedback = null)
    {
        var svc = Services.FirstOrDefault(s => s.ServiceId == serviceId) ?? new HappinessService { ServiceId = serviceId };
        svc.SatisfactionScore = score;
        svc.Feedback = feedback;
        svc.SurveyDate = DateTime.UtcNow;
        if (!Services.Contains(svc)) Services.Add(svc);
        Touch();
    }
}
