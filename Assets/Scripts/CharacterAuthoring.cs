using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using UnityEngine;

public class CharacterAuthoring : MonoBehaviour
{
    public float speed = 2.5f;
    public int hitPoints;

    public class Baker : Baker<CharacterAuthoring>
    {
        public override void Bake(CharacterAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new InitializeCharacterFlag());
            AddComponent(entity, new CharacterMoveDirection());
            AddComponent(entity, new CharacterMoveSpeed()
            {
                Value = authoring.speed
            });
            AddComponent(entity, new FacingDirectionOverride()
            {
                Value = 1
            });
            AddComponent(entity, new CharacterMaxHitPoints()
            {
                Value = authoring.hitPoints
            });
            AddComponent(entity, new CharacterCurrentHitPoints()
            {
                Value = authoring.hitPoints
            });
            // 类似于一个数组
            AddBuffer<DamageThisFrame>(entity);
            AddComponent<DestroyEntityFlag>(entity);
            SetComponentEnabled<DestroyEntityFlag>(entity, false);
        }
    }
}

#region Damage

public struct CharacterMaxHitPoints : IComponentData
{
    public int Value;
}

public struct CharacterCurrentHitPoints : IComponentData
{
    public int Value;
}

public struct DamageThisFrame : IBufferElementData
{
    public int Value;
}

public partial struct ProcessDamageThisFrameSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (hitPoints, damageThisFrame, entity) in
                 SystemAPI.Query<RefRW<CharacterCurrentHitPoints>, DynamicBuffer<DamageThisFrame>>()
                     .WithPresent<DestroyEntityFlag>().WithEntityAccess())
        {
            if (damageThisFrame.IsEmpty)
            {
                continue;
            }

            foreach (var damage in damageThisFrame)
            {
                hitPoints.ValueRW.Value -= damage.Value;
            }

            damageThisFrame.Clear();

            if (hitPoints.ValueRO.Value <= 0)
            {
                SystemAPI.SetComponentEnabled<DestroyEntityFlag>(entity, true);
            }
        }
    }
}

#endregion

#region Character Movement

public struct CharacterMoveDirection : IComponentData
{
    public float2 value;
}

public struct CharacterMoveSpeed : IComponentData
{
    public float Value;
}

public partial struct CharacterMoveSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState systemState)
    {
        foreach (var (velocity,
                     direction,
                     moveSpeed,
                     facingDirectionOverride,
                     entity) in
                 SystemAPI
                     .Query<RefRW<PhysicsVelocity>,
                         CharacterMoveDirection,
                         CharacterMoveSpeed,
                         RefRW<FacingDirectionOverride>>().WithEntityAccess())
        {
            var moveStep2d = direction.value * moveSpeed.Value;
            velocity.ValueRW.Linear = new float3(moveStep2d, 0f);

            if (math.abs(moveStep2d.x) > 0.15f)
            {
                facingDirectionOverride.ValueRW.Value = math.sign(moveStep2d.x);
            }

            if (SystemAPI.HasComponent<PlayerTag>(entity))
            {
                var playerAnimation = SystemAPI.GetComponentRW<AnimationIndexOverride>(entity);
                var animationIndex = math.lengthsq(moveStep2d) > float.Epsilon
                    ? PlayerAnimationIndex.Movement
                    : PlayerAnimationIndex.Idle;
                playerAnimation.ValueRW.Value = (float)animationIndex;
            }
        }
    }
}

#endregion

#region Character Initialization

public struct InitializeCharacterFlag : IComponentData, IEnableableComponent
{
}

// 初始化组（Initialization）→ 模拟组（Simulation）→ 呈现组（Presentation）
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct CharacterInitializationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState systemState)
    {
        foreach (var (mass, shouldInitialize) in SystemAPI
                     .Query<RefRW<PhysicsMass>, EnabledRefRW<InitializeCharacterFlag>>())
        {
            mass.ValueRW.InverseInertia = float3.zero;
            shouldInitialize.ValueRW = false;
        }
    }
}

#endregion

#region Presentation

// 这个属性会修改材质实际值
[MaterialProperty("_FacingDirection")]
public struct FacingDirectionOverride : IComponentData
{
    public float Value;
}

// 根据名称获取本身的Shader属性进行设置
public partial struct GlobalTimeUpdateSystem : ISystem
{
    private static int _globalTimeShaderPropertyID;

    public void OnCreate(ref SystemState state)
    {
        _globalTimeShaderPropertyID = Shader.PropertyToID("_GlobalTime");
    }

    public void OnUpdate(ref SystemState state)
    {
        Shader.SetGlobalFloat(_globalTimeShaderPropertyID, (float)SystemAPI.Time.ElapsedTime);
    }
}

#endregion