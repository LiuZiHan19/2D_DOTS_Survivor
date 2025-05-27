using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public class EnemySpawnerAuthoring : MonoBehaviour
{
    public GameObject EnemyPrefab;
    public float SpawnInterval;
    public float SpawnDistance;
    public uint RandomSeed;

    public class Baker : Baker<EnemySpawnerAuthoring>
    {
        public override void Bake(EnemySpawnerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new EnemySpawnData()
            {
                EnemyPrefab = GetEntity(authoring.EnemyPrefab, TransformUsageFlags.Dynamic),
                SpawnInterval = authoring.SpawnInterval,
                SpawnDistance = authoring.SpawnDistance
            });

            AddComponent(entity, new EnemySpawnState()
            {
                SpawnTimer = 0f,
                Random = Random.CreateFromIndex(authoring.RandomSeed)
            });
        }
    }
}

public struct EnemySpawnData : IComponentData
{
    public Entity EnemyPrefab;
    public float SpawnInterval;
    public float SpawnDistance;
}

public struct EnemySpawnState : IComponentData
{
    public float SpawnTimer;
    public Random Random;
}

[BurstCompile]
public partial struct EnemySpawnSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var deltaTime = SystemAPI.Time.DeltaTime;
        var ecbSystem = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged);

        var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
        var playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;
        foreach (var (spawnData, spawnState) in SystemAPI
                     .Query<RefRO<EnemySpawnData>, RefRW<EnemySpawnState>>())
        {
            spawnState.ValueRW.SpawnTimer -= deltaTime;

            if (spawnState.ValueRW.SpawnTimer > 0f)
            {
                continue;
            }

            spawnState.ValueRW.SpawnTimer = spawnData.ValueRO.SpawnInterval;

            var enemyEntity = ecb.Instantiate(spawnData.ValueRO.EnemyPrefab);
            var spawnAngle = spawnState.ValueRW.Random.NextFloat(0f, math.TAU);
            var spawnPosition = new float3()
            {
                x = math.sin(spawnAngle),
                y = math.cos(spawnAngle),
                z = 0
            };
            spawnPosition *= spawnData.ValueRO.SpawnDistance;
            spawnPosition += playerPosition;

            ecb.SetComponent(enemyEntity, LocalTransform.FromPositionRotation(spawnPosition, quaternion.identity));
        }
    }
}