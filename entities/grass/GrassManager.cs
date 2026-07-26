using Godot;
using System.Collections.Generic;

public partial class GrassManager : Node
{
    public static GrassManager Instance { get; private set; }

    [Export] private Mesh _grassMesh;
    [Export] private Material _material;
    [Export] private int _poolSize = 1600;
    [Export] private int _radiusCells = 10;
    [Export] private int _instancesPerTile = 4;
    [Export] private float _jitterRadius = 0.4f;
    [Export] private float _minScale = 0.12f;
    [Export] private float _maxScale = 0.18f;
    [Export] private float _minHeight = 0.8f;
    [Export] private float _maxHeight = 1.3f;

    private static readonly Transform3D _hidden = new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero);

    private Rid _multiMeshRid;
    private Rid _instanceRid;
    private Stack<int> _freeSlots;
    private readonly Dictionary<Vector2I, int[]> _tileSlots = new();
    private Vector2I _lastPlayerCell = new(int.MinValue, int.MinValue);
    private Vector3 _localOffset;

    public override void _Ready(){
        Instance = this;

        Aabb aabb = _grassMesh.GetAabb();
        _localOffset = new Vector3(
            aabb.Position.X + aabb.Size.X / 2f,
            aabb.Position.Y,
            aabb.Position.Z + aabb.Size.Z / 2f);

        _freeSlots = new Stack<int>(_poolSize);
        for (int i = _poolSize - 1; i >= 0; i--)
            _freeSlots.Push(i);

        _multiMeshRid = RenderingServer.MultimeshCreate();
        RenderingServer.MultimeshAllocateData(_multiMeshRid, _poolSize, RenderingServer.MultimeshTransformFormat.Transform3D, false, false);
        RenderingServer.MultimeshSetMesh(_multiMeshRid, _grassMesh.GetRid());
        for (int i = 0; i < _poolSize; i++)
            RenderingServer.MultimeshInstanceSetTransform(_multiMeshRid, i, _hidden);

        Rid scenario = GetViewport().World3D.Scenario;
        _instanceRid = RenderingServer.InstanceCreate2(_multiMeshRid, scenario);
        RenderingServer.InstanceSetTransform(_instanceRid, Transform3D.Identity);
        RenderingServer.InstanceGeometrySetMaterialOverride(_instanceRid, _material.GetRid());
        RenderingServer.InstanceGeometrySetCastShadowsSetting(_instanceRid, RenderingServer.ShadowCastingSetting.Off);
    }

    public override void _Process(double delta){
        if (Player.Instance == null || FireManager.Instance == null) return;

        Vector2I currentCell = FireManager.Instance.GetGridCoordAt(Player.Instance.GlobalPosition);
        if (currentCell == _lastPlayerCell) return;

        _lastPlayerCell = currentCell;
        Refresh();
    }

    private void Refresh(){
        var target = new Dictionary<Vector2I, Vector3>();
        foreach (var (gridCoord, worldPos, state) in FireManager.Instance.GetCellsAround(Player.Instance.GlobalPosition, _radiusCells))
            if (state == CellState.Unburnt) target[gridCoord] = worldPos;

        foreach (var gridCoord in new List<Vector2I>(_tileSlots.Keys))
            if (!target.ContainsKey(gridCoord)) ReleaseTile(gridCoord);

        foreach (var (gridCoord, worldPos) in target){
            if (_tileSlots.ContainsKey(gridCoord)) continue;
            if (_freeSlots.Count < _instancesPerTile) continue;
            AssignTile(gridCoord, worldPos);
        }
    }

    private void AssignTile(Vector2I gridCoord, Vector3 worldPos){
        int[] slots = new int[_instancesPerTile];
        for (int i = 0; i < _instancesPerTile; i++){
            int slot = _freeSlots.Pop();
            slots[i] = slot;

            Vector3 jitter = new Vector3(
                (float)GD.RandRange(-_jitterRadius, _jitterRadius), 0f,
                (float)GD.RandRange(-_jitterRadius, _jitterRadius));
            float rot = (float)GD.RandRange(0f, Mathf.Tau);
            float scale = (float)GD.RandRange(_minScale, _maxScale);
            float height = (float)GD.RandRange(_minHeight, _maxHeight);
            var basis = Basis.Identity.Rotated(Vector3.Up, rot).Scaled(Vector3.One * scale).Scaled(new Vector3(1f, height, 1f));
            var xform = new Transform3D(basis, worldPos + jitter).TranslatedLocal(-_localOffset);
            RenderingServer.MultimeshInstanceSetTransform(_multiMeshRid, slot, xform);
        }
        _tileSlots[gridCoord] = slots;
    }

    private void ReleaseTile(Vector2I gridCoord){
        foreach (int slot in _tileSlots[gridCoord]){
            RenderingServer.MultimeshInstanceSetTransform(_multiMeshRid, slot, _hidden);
            _freeSlots.Push(slot);
        }
        _tileSlots.Remove(gridCoord);
    }

    public override void _ExitTree(){
        if (_instanceRid.IsValid) RenderingServer.FreeRid(_instanceRid);
        if (_multiMeshRid.IsValid) RenderingServer.FreeRid(_multiMeshRid);
        if (Instance == this) Instance = null;
    }
}
