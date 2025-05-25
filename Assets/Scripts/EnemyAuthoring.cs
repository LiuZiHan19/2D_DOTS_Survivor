using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

[RequireComponent(typeof(CharacterAuthoring))]
public class EnemyAuthoring : MonoBehaviour
{
    public int AttackDamage;
    public float CooldownTime;

    public class Baker : Baker<EnemyAuthoring>
    {
        public override void Bake(EnemyAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EnemyTag());
            AddComponent(entity, new EnemyAttackData()
            {
                HitPoints = authoring.AttackDamage,
                CooldownTime = authoring.CooldownTime
            });
            AddComponent(entity, new EnemyCooldownExpirationTimestamp());
            SetComponentEnabled<EnemyCooldownExpirationTimestamp>(entity, false);
        }
    }
}

public struct EnemyTag : IComponentData
{
}

#region Move To Player

public partial struct EnemyMoveToPlayerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
        var playerPosition = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO.Position.xy;

        EnemyMoveToPlayerJob1 job = new EnemyMoveToPlayerJob1
        {
            PlayerPosition = playerPosition
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(EnemyTag))]
public partial struct EnemyMoveToPlayerJob1 : IJobEntity
{
    public float2 PlayerPosition;

    private void Execute(ref CharacterMoveDirection direction, in LocalTransform transform)
    {
        var vectorToPlayer = PlayerPosition - transform.Position.xy;
        direction.value = math.normalize(vectorToPlayer);
    }
}

#endregion

#region Attack

public struct EnemyAttackData : IComponentData
{
    public int HitPoints;
    public float CooldownTime;
}

public struct EnemyCooldownExpirationTimestamp : IComponentData, IEnableableComponent
{
    public double Value;
}

[UpdateInGroup(typeof(PhysicsSystemGroup))]
[UpdateAfter(typeof(PhysicsSimulationGroup))]
[UpdateBefore(typeof(AfterPhysicsSystemGroup))]
public partial struct EnemyAttackSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SimulationSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var elapsedTime = SystemAPI.Time.ElapsedTime;
        foreach (var (expirationTimestamp, cooldownEnable) in
                 SystemAPI.Query<EnemyCooldownExpirationTimestamp, EnabledRefRW<EnemyCooldownExpirationTimestamp>>())
        {
            if (expirationTimestamp.Value > elapsedTime)
            {
                continue;
            }

            cooldownEnable.ValueRW = false;
        }

        var attackJob = new EnemyAttackJob1()
        {
            PlayerLookup = SystemAPI.GetComponentLookup<PlayerTag>(true),
            AttackDataLookup = SystemAPI.GetComponentLookup<EnemyAttackData>(true),
            CooldownExpirationTimestampLookup = SystemAPI.GetComponentLookup<EnemyCooldownExpirationTimestamp>(),
            DamageBufferLookup = SystemAPI.GetBufferLookup<DamageThisFrame>(),
            ElapsedTime = elapsedTime
        };

        var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
        state.Dependency = attackJob.Schedule(simulationSingleton, state.Dependency);
    }
}

[BurstCompile]
public struct EnemyAttackJob1 : ICollisionEventsJob
{
    [ReadOnly] public ComponentLookup<PlayerTag> PlayerLookup;
    [ReadOnly] public ComponentLookup<EnemyAttackData> AttackDataLookup;
    public ComponentLookup<EnemyCooldownExpirationTimestamp> CooldownExpirationTimestampLookup;
    public BufferLookup<DamageThisFrame> DamageBufferLookup;
    public double ElapsedTime;

    public void Execute(CollisionEvent collisionEvent)
    {
        Entity playerEntity;
        Entity enemyEntity;
        if (PlayerLookup.HasComponent(collisionEvent.EntityA) &&
            AttackDataLookup.HasComponent(collisionEvent.EntityB))
        {
            playerEntity = collisionEvent.EntityA;
            enemyEntity = collisionEvent.EntityB;
        }
        else if (PlayerLookup.HasComponent(collisionEvent.EntityB) &&
                 AttackDataLookup.HasComponent(collisionEvent.EntityA))
        {
            playerEntity = collisionEvent.EntityB;
            enemyEntity = collisionEvent.EntityA;
        }
        else
        {
            return;
        }

        if (CooldownExpirationTimestampLookup.IsComponentEnabled(enemyEntity))
        {
            return;
        }

        var attackData = AttackDataLookup[enemyEntity];
        // 攻击后将timer = 当前流逝的时间+冷却
        CooldownExpirationTimestampLookup[enemyEntity] = new EnemyCooldownExpirationTimestamp()
        {
            Value = ElapsedTime + attackData.CooldownTime
        };
        CooldownExpirationTimestampLookup.SetComponentEnabled(enemyEntity, true);

        var playerDamageBuffer = DamageBufferLookup[playerEntity];
        playerDamageBuffer.Add(new DamageThisFrame()
        {
            Value = attackData.HitPoints
        });
    }
}

#endregion