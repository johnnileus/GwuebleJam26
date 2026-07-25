using Godot;

[GlobalClass]
public partial class ParticleLayerConfig : Resource
{
    [Export] public Mesh Mesh;
    [Export] public float MeshScale = 0.1f;
    [Export] public ShaderMaterial MultiMeshMaterial;
    [Export] public Vector3 ConstantForce;
    [Export] public float Lifetime = 5.0f;
    [Export] public float SpawnRate = 20.0f;
    [Export] public Vector2 Size;
    [Export] public int ParticlesPerCell = 24;
}
