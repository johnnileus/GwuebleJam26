using Godot;

public partial class HexGridRenderer : Node
{
    public static HexGridRenderer Instance { get; private set; }

    [Export] private Mesh _hexMesh;
    [Export] private Material _material;
    [Export] private float _yRotationDegrees = 90f;

    private Rid _multiMeshRid;
    private Rid _instanceRid;

    public override void _Ready(){
        Instance = this;
    }

    public void Initialize(int totalCells){
        _multiMeshRid = RenderingServer.MultimeshCreate();
        RenderingServer.MultimeshAllocateData(_multiMeshRid, totalCells, RenderingServer.MultimeshTransformFormat.Transform3D, true, false);
        RenderingServer.MultimeshSetMesh(_multiMeshRid, _hexMesh.GetRid());

        Rid scenario = GetViewport().World3D.Scenario;
        _instanceRid = RenderingServer.InstanceCreate2(_multiMeshRid, scenario);
        RenderingServer.InstanceSetTransform(_instanceRid, Transform3D.Identity);
        RenderingServer.InstanceGeometrySetMaterialOverride(_instanceRid, _material.GetRid());
        RenderingServer.InstanceGeometrySetCastShadowsSetting(_instanceRid, RenderingServer.ShadowCastingSetting.Off);
    }

    public void SetCellTransform(int index, Vector3 worldPos){
        var basis = Basis.Identity.Rotated(Vector3.Up, Mathf.DegToRad(_yRotationDegrees));
        RenderingServer.MultimeshInstanceSetTransform(_multiMeshRid, index, new Transform3D(basis, worldPos).Translated(Vector3.Up * (float)GD.RandRange(-0.1f, 0.1f)));
    }

    public void SetCellColor(int index, Color color){
        RenderingServer.MultimeshInstanceSetColor(_multiMeshRid, index, color);
    }

    public override void _ExitTree(){
        if (_instanceRid.IsValid) RenderingServer.FreeRid(_instanceRid);
        if (_multiMeshRid.IsValid) RenderingServer.FreeRid(_multiMeshRid);
        if (Instance == this) Instance = null;
    }
}
