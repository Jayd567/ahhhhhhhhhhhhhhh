using Unity.Entities;

namespace ColonySim.Simulation.Ticking
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SimulationTickGroup : ComponentSystemGroup
    {
        protected override void OnUpdate()
        {
            if (!SystemAPI.HasSingleton<SimulationTick>()) return;
            base.OnUpdate();
            SimulationTick tick = SystemAPI.GetSingleton<SimulationTick>();
            tick.Value += 1;
            SystemAPI.SetSingleton(tick);
        }
    }
}
