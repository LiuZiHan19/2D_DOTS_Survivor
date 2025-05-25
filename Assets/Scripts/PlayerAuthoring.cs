using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour
{
    public GameObject AttackPrefab;
    public float CooldownTime;
    public float DetectionSize;

    public class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent<PlayerTag>(entity);
            AddComponent<CameraTarget>(entity);
            AddComponent<InitializeCameraTargetTag>(entity);
            AddComponent(entity, new AnimationIndexOverride()
            {
                Value = (float)PlayerAnimationIndex.Idle
            });

            var enemyLayer = LayerMask.NameToLayer("Enemy");
            // 计算 2 的 enemyLayer 次方，生成一个二进制掩码（LayerMask）
            var enemyLayerMask = (uint)math.pow(2, enemyLayer);

            var attackCollisionFilter = new CollisionFilter()
            {
                BelongsTo = uint.MaxValue,
                CollidesWith = enemyLayerMask
            };

            AddComponent(entity, new PlayerAttackData()
            {
                AttackPrefab = GetEntity(authoring.AttackPrefab, TransformUsageFlags.Dynamic),
                CooldownTime = authoring.CooldownTime,
                DetectionSize = new float3(authoring.DetectionSize),
                CollisionFilter = attackCollisionFilter
            });
            AddComponent<PlayerCooldownExpirationTimestamp>(entity);
        }
    }
}

public struct PlayerTag : IComponentData
{
}

public partial class PlayerInputSystem : SystemBase
{
    private SurvivorInput _input;

    protected override void OnCreate()
    {
        _input = new SurvivorInput();
        _input.Enable();
    }

    protected override void OnUpdate()
    {
        var currentInput = (float2)_input.Player.Move.ReadValue<Vector2>();
        foreach (var direction in SystemAPI.Query<RefRW<CharacterMoveDirection>>().WithAll<PlayerTag>())
        {
            direction.ValueRW.value = currentInput;
        }
    }
}

#region Camera

public struct CameraTarget : IComponentData
{
    public UnityObjectRef<Transform> CameraTransform;
}

public struct InitializeCameraTargetTag : IComponentData
{
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct CameraInitializationSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<InitializeCameraTargetTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (CameraTargetManager.Instance == null) return;
        var cameraTargetTransform = CameraTargetManager.Instance.transform;

        var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
        foreach (var (camera, entity) in SystemAPI.Query<RefRW<CameraTarget>>()
                     .WithAll<InitializeCameraTargetTag, PlayerTag>().WithEntityAccess())
        {
            camera.ValueRW.CameraTransform = cameraTargetTransform;
            ecb.RemoveComponent<InitializeCameraTargetTag>(entity);
        }

        ecb.Playback(state.EntityManager);
    }
}

[UpdateAfter(typeof(TransformSystemGroup))]
public partial struct MoveCameraSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (transform, cameraTarget) in SystemAPI.Query<LocalToWorld, CameraTarget>()
                     .WithAll<PlayerTag>().WithNone<InitializeCameraTargetTag>())
        {
            cameraTarget.CameraTransform.Value.position = transform.Position;
        }
    }
}

#endregion

#region Presentation

[MaterialProperty("_AnimationIndex")]
public struct AnimationIndexOverride : IComponentData
{
    public float Value;
}

public enum PlayerAnimationIndex : byte
{
    Movement = 0,
    Idle = 1,
    None = byte.MaxValue
}

#endregion

public struct PlayerAttackData : IComponentData
{
    public Entity AttackPrefab;
    public float CooldownTime;
    public float3 DetectionSize;
    public CollisionFilter CollisionFilter;
}

public struct PlayerCooldownExpirationTimestamp : IComponentData
{
    public double Value;
}

public partial struct PlayerAttackSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var elapsedTime = SystemAPI.Time.ElapsedTime;
        var ecbSystem = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged);
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

        foreach (var (expiration, attackData, transform) in SystemAPI
                     .Query<RefRW<PlayerCooldownExpirationTimestamp>, PlayerAttackData, LocalTransform>())
        {
            if (expiration.ValueRO.Value > elapsedTime)
            {
                continue;
            }

            var spawnPosition = transform.Position;
            var minDetectionPositon = spawnPosition - attackData.DetectionSize;
            var maxDetectionPositon = spawnPosition + attackData.DetectionSize;

            var aabbInput = new OverlapAabbInput()
            {
                Aabb = new Aabb()
                {
                    Min = minDetectionPositon,
                    Max = maxDetectionPositon
                },
                Filter = attackData.CollisionFilter
            };
            var overlapHits = new NativeList<int>(state.WorldUpdateAllocator);
            if (physicsWorld.OverlapAabb(aabbInput, ref overlapHits) == false)
            {
                continue;
            }

            var maxDistanceSq = float.MaxValue;
            var closestEnemyPosition = float3.zero;
            foreach (var overlapHit in overlapHits)
            {
                var currentEnemyPos = physicsWorld.Bodies[overlapHit].WorldFromBody.pos;
                var distanceToPlayerSq = math.distancesq(spawnPosition.xy, currentEnemyPos.xy);
                if (distanceToPlayerSq < maxDistanceSq)
                {
                    maxDistanceSq = distanceToPlayerSq;
                    closestEnemyPosition = currentEnemyPos;
                }
            }

            var vectorToClosestEnemy = closestEnemyPosition - spawnPosition;
            var angleToClosestEnemy = math.atan2(vectorToClosestEnemy.y, vectorToClosestEnemy.x);
            var spawnOrientation = quaternion.Euler(0f, 0f, angleToClosestEnemy);

            var newAttack = ecb.Instantiate(attackData.AttackPrefab);
            ecb.SetComponent(newAttack, LocalTransform.FromPositionRotation(spawnPosition, spawnOrientation));

            expiration.ValueRW.Value = elapsedTime + attackData.CooldownTime;
        }
    }
}