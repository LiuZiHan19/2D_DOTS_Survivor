using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

public class PlasmaBlastAuthoring : MonoBehaviour
{
    public float MoveSpeed;
    public int AttackDamage;

    public class Baker : Baker<PlasmaBlastAuthoring>
    {
        public override void Bake(PlasmaBlastAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PlasmaBlastData()
            {
                MoveSpeed = authoring.MoveSpeed,
                AttackDamage = authoring.AttackDamage
            });
            AddComponent<DestroyEntityFlag>(entity);
            SetComponentEnabled<DestroyEntityFlag>(entity, false);
        }
    }
}

public struct PlasmaBlastData : IComponentData
{
    public float MoveSpeed;
    public int AttackDamage;
}

public partial struct MovePlasmaBlastSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (transform, plasmaBlastData) in
                 SystemAPI.Query<RefRW<LocalTransform>, PlasmaBlastData>())
        {
            transform.ValueRW.Position +=
                transform.ValueRO.Right() * plasmaBlastData.MoveSpeed * deltaTime;
        }
    }
}

[UpdateInGroup(typeof(PhysicsSystemGroup))]
[UpdateAfter(typeof(PhysicsSimulationGroup))]
[UpdateBefore(typeof(AfterPhysicsSystemGroup))]
public partial struct PlasmaBlastAttackSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var plasmaBlastDataLookup = SystemAPI.GetComponentLookup<PlasmaBlastData>(true);
        var enemyTagLookup = SystemAPI.GetComponentLookup<EnemyTag>(true);
        var damageBufferLookup = SystemAPI.GetBufferLookup<DamageThisFrame>();
        var destroyEntityFlagLookup = SystemAPI.GetComponentLookup<DestroyEntityFlag>();

        PlasmaBlastAttackJob1 attackJob1 = new PlasmaBlastAttackJob1()
        {
            PlasmaBlastDataLookup = plasmaBlastDataLookup,
            EnemyTagLookup = enemyTagLookup,
            DamageBufferLookup = damageBufferLookup,
            DestroyEntityFlagLookup = destroyEntityFlagLookup
        };

        var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
        state.Dependency = attackJob1.Schedule(simulationSingleton, state.Dependency);
    }
}

public struct PlasmaBlastAttackJob1 : ITriggerEventsJob
{
    [ReadOnly] public ComponentLookup<PlasmaBlastData> PlasmaBlastDataLookup;
    [ReadOnly] public ComponentLookup<EnemyTag> EnemyTagLookup;
    public BufferLookup<DamageThisFrame> DamageBufferLookup;
    public ComponentLookup<DestroyEntityFlag> DestroyEntityFlagLookup;

    public void Execute(TriggerEvent triggerEvent)
    {
        Entity plasmaBlastEntity;
        Entity enemyEntity;

        if (PlasmaBlastDataLookup.HasComponent(triggerEvent.EntityA) &&
            EnemyTagLookup.HasComponent(triggerEvent.EntityB))
        {
            plasmaBlastEntity = triggerEvent.EntityA;
            enemyEntity = triggerEvent.EntityB;
        }
        else if (PlasmaBlastDataLookup.HasComponent(triggerEvent.EntityB) &&
                 EnemyTagLookup.HasComponent(triggerEvent.EntityA))
        {
            plasmaBlastEntity = triggerEvent.EntityB;
            enemyEntity = triggerEvent.EntityA;
        }
        else
        {
            return;
        }

        var attackData = PlasmaBlastDataLookup[plasmaBlastEntity];
        var enemyDamageBuffer = DamageBufferLookup[enemyEntity];
        enemyDamageBuffer.Add(new DamageThisFrame()
        {
            Value = attackData.AttackDamage
        });

        DestroyEntityFlagLookup.SetComponentEnabled(plasmaBlastEntity, true);
    }
}