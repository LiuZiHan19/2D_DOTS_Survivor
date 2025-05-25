using TMG.Survivors;
using Unity.Entities;

public partial struct DestroyEntitySystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var endEcbSystem = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var endEcb = endEcbSystem.CreateCommandBuffer(state.WorldUnmanaged);
        foreach (var (_, entity) in SystemAPI.Query<DestroyEntityFlag>().WithEntityAccess()) // 查找启用的
        {
            if (SystemAPI.HasComponent<PlayerTag>(entity))
            {
                GameUIController.Instance.ShowGameOverUI();
            }

            endEcb.DestroyEntity(entity);
        }
    }
}

public struct DestroyEntityFlag : IComponentData, IEnableableComponent
{
}