using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;

public class GemAuthoring : MonoBehaviour
{
    private class Baker : Baker<GemAuthoring>
    {
        public override void Bake(GemAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new GemTag());
            AddComponent(entity, new DestroyEntityFlag());
            SetComponentEnabled<DestroyEntityFlag>(entity, false);
        }
    }
}

public struct GemTag : IComponentData
{
}

[BurstCompile]
public partial struct CollectGemSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SimulationSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var collectJob = new CollectGemJob()
        {
            GemLookup = SystemAPI.GetComponentLookup<GemTag>(),
            GemCollectedCountLookup = SystemAPI.GetComponentLookup<GemCollectedCount>(),
            DestroyEntityLookup = SystemAPI.GetComponentLookup<DestroyEntityFlag>(),
            UpdateGemUILookup = SystemAPI.GetComponentLookup<UpdateGemUIFlag>()
        };

        var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
        state.Dependency = collectJob.Schedule(simulationSingleton, state.Dependency);
    }
}

[BurstCompile]
public struct CollectGemJob : ITriggerEventsJob
{
    [ReadOnly] public ComponentLookup<GemTag> GemLookup;
    public ComponentLookup<GemCollectedCount> GemCollectedCountLookup;
    public ComponentLookup<DestroyEntityFlag> DestroyEntityLookup;
    public ComponentLookup<UpdateGemUIFlag> UpdateGemUILookup;

    [BurstCompile]
    public void Execute(TriggerEvent triggerEvent)
    {
        Entity player;
        Entity gem;
        if (GemLookup.HasComponent(triggerEvent.EntityA) &&
            GemCollectedCountLookup.HasComponent(triggerEvent.EntityB))
        {
            player = triggerEvent.EntityB;
            gem = triggerEvent.EntityA;
        }
        else if (GemLookup.HasComponent(triggerEvent.EntityB) &&
                 GemCollectedCountLookup.HasComponent(triggerEvent.EntityA))
        {
            player = triggerEvent.EntityA;
            gem = triggerEvent.EntityB;
        }
        else
        {
            return;
        }

        var gemCollected = GemCollectedCountLookup[player];
        gemCollected.Value++;
        GemCollectedCountLookup[player] = gemCollected;

        UpdateGemUILookup.SetComponentEnabled(player, true);

        DestroyEntityLookup.SetComponentEnabled(gem, true);
    }
}