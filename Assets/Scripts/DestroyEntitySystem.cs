using TMG.Survivors;
using Unity.Entities;
using Unity.Transforms;

public partial struct DestroyEntitySystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var endEcbSystem = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var endEcb = endEcbSystem.CreateCommandBuffer(state.WorldUnmanaged);
        var beginEcbSystem = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
        var beginEcb = beginEcbSystem.CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (_, entity) in SystemAPI.Query<DestroyEntityFlag>().WithEntityAccess()) // 查找启用的
        {
            if (SystemAPI.HasComponent<PlayerTag>(entity))
            {
                GameUIController.Instance.ShowGameOverUI();
            }

            if (SystemAPI.HasComponent<GemPrefab>(entity))
            {
                var gemPrefab = SystemAPI.GetComponentRW<GemPrefab>(entity).ValueRW.Value;
                var newGem = beginEcb.Instantiate(gemPrefab);

                var spawnPos = SystemAPI.GetComponent<LocalToWorld>(entity).Position;
                beginEcb.SetComponent(newGem, LocalTransform.FromPosition(spawnPos));
            }

            endEcb.DestroyEntity(entity);
        }
    }
}

public struct DestroyEntityFlag : IComponentData, IEnableableComponent
{
}