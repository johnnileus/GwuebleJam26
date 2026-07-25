using Godot;
using System.Collections.Generic;

public partial class FireParticlePool : Node
{
    public static FireParticlePool Instance { get; private set; }

    public int MaxConcurrentCells => _maxConcurrentCells;
    public int ActiveSlotCount => _maxConcurrentCells - _freeSlots.Count;

    [Export] private int _maxConcurrentCells = 512;
    [Export] private ParticleLayerConfig _fireLayerConfig;
    [Export] private ParticleLayerConfig _smokeLayerConfig;

    private class LayerRuntime
    {
        public ParticleLayerConfig Config;
        public Rid MultiMeshRid;
        public Rid InstanceRid;
        public float[] Lifetimes;
        public float[] SpawnAccumulator;
        public int[] SpawnCursor;
    }

    private LayerRuntime _fire;
    private LayerRuntime _smoke;
    private Vector3[] _slotWorldPos;
    private Stack<int> _freeSlots;
    private readonly List<int> _dyingSlots = new();

    public override void _Ready()
    {
        Instance = this;

        _slotWorldPos = new Vector3[_maxConcurrentCells];
        _freeSlots = new Stack<int>(_maxConcurrentCells);
        for (int i = _maxConcurrentCells - 1; i >= 0; i--)
            _freeSlots.Push(i);

        Rid scenario = GetViewport().World3D.Scenario;
        _fire = CreateLayer(_fireLayerConfig, scenario);
        _smoke = CreateLayer(_smokeLayerConfig, scenario);
    }

    private LayerRuntime CreateLayer(ParticleLayerConfig config, Rid scenario)
    {
        int total = _maxConcurrentCells * config.ParticlesPerCell;

        Rid multiMeshRid = RenderingServer.MultimeshCreate();
        RenderingServer.MultimeshAllocateData(multiMeshRid, total, RenderingServer.MultimeshTransformFormat.Transform3D, false, true);
        RenderingServer.MultimeshSetMesh(multiMeshRid, config.Mesh.GetRid());

        for (int i = 0; i < total; i++) {
            RenderingServer.MultimeshInstanceSetTransform(multiMeshRid, i, Transform3D.Identity);
            RenderingServer.MultimeshInstanceSetCustomData(multiMeshRid, i, new Color(0f, 0f, 0f, 0f));
        }

        Rid instanceRid = RenderingServer.InstanceCreate2(multiMeshRid, scenario);
        RenderingServer.InstanceSetTransform(instanceRid, Transform3D.Identity);
        RenderingServer.InstanceGeometrySetMaterialOverride(instanceRid, config.MultiMeshMaterial.GetRid());
        RenderingServer.InstanceGeometrySetCastShadowsSetting(instanceRid, RenderingServer.ShadowCastingSetting.Off);

        config.MultiMeshMaterial.SetShaderParameter("constant_force", config.ConstantForce);
        config.MultiMeshMaterial.SetShaderParameter("lifetime", config.Lifetime);

        return new LayerRuntime {
            Config = config,
            MultiMeshRid = multiMeshRid,
            InstanceRid = instanceRid,
            Lifetimes = new float[total],
            SpawnAccumulator = new float[_maxConcurrentCells],
            SpawnCursor = new int[_maxConcurrentCells],
        };
    }

    public int AcquireSlot(Vector3 worldPos){
        if (_freeSlots.Count == 0) return -1;

        int slot = _freeSlots.Pop();
        _slotWorldPos[slot] = worldPos;
        return slot;
    }

    public void UpdateCell(int slot, Vector3 worldPos, float intensity, double delta){
        if (slot < 0) return;

        _slotWorldPos[slot] = worldPos;
        UpdateLayer(_fire, slot, intensity, delta, spawning: true);
        UpdateLayer(_smoke, slot, 1.0f, delta, spawning: true);
    }

    public void Release(int slot){
        if (slot < 0) return;
        _dyingSlots.Add(slot);
    }

    public void AdvanceDyingSlots(double delta){
        for (int i = _dyingSlots.Count - 1; i >= 0; i--){
            int slot = _dyingSlots[i];
            UpdateLayer(_fire, slot, 0f, delta, spawning: false);
            UpdateLayer(_smoke, slot, 0f, delta, spawning: false);

            if (IsSlotDead(_fire, slot) && IsSlotDead(_smoke, slot)) {
                _dyingSlots.RemoveAt(i);
                _freeSlots.Push(slot);
            }
        }
    }

    private void UpdateLayer(LayerRuntime rt, int slot, float intensity, double delta, bool spawning){
        float dt = (float)delta;
        int count = rt.Config.ParticlesPerCell;
        int baseIdx = slot * count;

        for (int local = 0; local < count; local++){
            int i = baseIdx + local;
            if (rt.Lifetimes[i] > 0f) {
                rt.Lifetimes[i] = Mathf.Max(0f, rt.Lifetimes[i] - dt);
                RenderingServer.MultimeshInstanceSetCustomData(rt.MultiMeshRid, i, new Color(rt.Lifetimes[i], 0f, 0f, 0f));
            }
        }

        if (!spawning) return;

        rt.SpawnAccumulator[slot] += dt * rt.Config.SpawnRate * intensity;
        int toSpawn = (int)Mathf.Floor(rt.SpawnAccumulator[slot]);
        rt.SpawnAccumulator[slot] -= toSpawn;

        int cursor = rt.SpawnCursor[slot];
        int attempts = 0;
        while (toSpawn > 0 && attempts < count){
            int i = baseIdx + cursor;
            if (rt.Lifetimes[i] <= 0f) {
                SpawnParticle(rt, slot, i);
                toSpawn--;
            }
            cursor = (cursor + 1) % count;
            attempts++;
        }
        rt.SpawnCursor[slot] = cursor;
    }

    private void SpawnParticle(LayerRuntime rt, int slot, int globalIndex){
        Vector3 origin = _slotWorldPos[slot];
        Vector3 jitter = new Vector3(
            (float)GD.RandRange(-rt.Config.Size.X / 2f, rt.Config.Size.X / 2f), 0f,
            (float)GD.RandRange(-rt.Config.Size.Y / 2f, rt.Config.Size.Y / 2f));

        var xform = new Transform3D(Basis.Identity.Scaled(Vector3.One * rt.Config.MeshScale), origin + jitter);
        RenderingServer.MultimeshInstanceSetTransform(rt.MultiMeshRid, globalIndex, xform);
        rt.Lifetimes[globalIndex] = rt.Config.Lifetime;
        RenderingServer.MultimeshInstanceSetCustomData(rt.MultiMeshRid, globalIndex, new Color(rt.Config.Lifetime, 0f, 0f, 0f));
    }

    private bool IsSlotDead(LayerRuntime rt, int slot){
        int count = rt.Config.ParticlesPerCell;
        int baseIdx = slot * count;
        for (int local = 0; local < count; local++)
            if (rt.Lifetimes[baseIdx + local] > 0f) return false;
        return true;
    }

    public override void _ExitTree(){
        FreeLayer(_fire);
        FreeLayer(_smoke);
        if (Instance == this) Instance = null;
    }

    private void FreeLayer(LayerRuntime rt){
        if (rt == null) return;
        if (rt.InstanceRid.IsValid) RenderingServer.FreeRid(rt.InstanceRid);
        if (rt.MultiMeshRid.IsValid) RenderingServer.FreeRid(rt.MultiMeshRid);
    }
}
