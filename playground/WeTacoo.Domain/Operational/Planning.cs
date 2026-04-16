namespace WeTacoo.Domain.Operational;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Operational.Entities;

public class Planning : AggregateRoot
{
    public Planning() { Id = NextId("plan"); }
    public DateTime Date { get; set; }
    public List<Resource> Resources { get; set; } = [];
    public List<PlanningTeam> Teams { get; set; } = [];
    public List<Mission> Missions { get; set; } = [];

    public Mission AddMission(string teamId, List<ServiceRef> serviceRefs, List<string> vehicleIds)
    {
        var mission = new Mission { TeamId = teamId, ServiceRefs = serviceRefs, VehicleResourceIds = vehicleIds };
        Missions.Add(mission);
        Touch();
        return mission;
    }
}
