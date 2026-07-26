using System;
using Godot;

public partial class Player : CharacterBody3D
{
    [Export] public float Speed = 5.0f;
    [Export] public float Acceleration = 40.0f;   
    [Export] public float RotationSpeed = 12.0f; 

    [Export] public Node3D Model;
    [Export] private PackedScene _waterSplash;

    [Export] private float _splashSpread;
    [Export] private float _splashSpeed;
    [Export] private float _splashUpSpeed;
    

    private FireManager _fireMgr;

    [Export] private float _waterGatherRate;
    private float _waterGathered = 80f;
    [Export] private float _maxWater = 80f;
    [Export] private float _waterPerUse = 10f;

    [Export] private Label3D _waterLabel;
    private bool _facingWater = false;
    
        
    //fire sfx
    private AudioStreamPlayer3D[] _firePlayers;
    [Export] private AudioStream _fireStream;

    public override void _Ready(){
        _fireMgr = FireManager.Instance;
        _waterLabel.Visible = false;
        
        
        _firePlayers = new AudioStreamPlayer3D[9];
        float spacing = 10f;
        for (int y = 0; y < 3; y++) {
            for (int x = 0; x < 3; x++) {
                var p = new AudioStreamPlayer3D {
                    Stream = _fireStream,
                    VolumeDb = -80f
                };
                AddChild(p);
                p.Play();
                _firePlayers[y * 3 + x] = p;
            }
        }
        
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 velocity = Velocity;

        if (!IsOnFloor())
            velocity += GetGravity() * dt;

        Vector2 inputDir = Input.GetVector("player_left", "player_right", "player_forward", "player_backward");
        Vector3 direction = new Vector3(inputDir.X, 0, inputDir.Y).Normalized();

        Vector3 targetVel = direction * Speed;
        velocity.X = Mathf.MoveToward(velocity.X, targetVel.X, Acceleration * dt);
        velocity.Z = Mathf.MoveToward(velocity.Z, targetVel.Z, Acceleration * dt);

        Velocity = velocity;
        MoveAndSlide();

        if (Model != null && direction != Vector3.Zero)
        {
            float targetAngle = Mathf.Atan2(direction.X, direction.Z);
            Model.Rotation = new Vector3(
                Model.Rotation.X,
                Mathf.LerpAngle(Model.Rotation.Y, targetAngle, RotationSpeed * dt),
                Model.Rotation.Z);
        }
    }

    public override void _Process(double delta){

        _facingWater = _fireMgr.GetCellAt(Position + Model.GlobalTransform.Basis.Z).State == CellState.Water;
        
        _waterLabel.Visible = _facingWater;

        if (Input.IsActionJustPressed("debug_ignite")) {
            _fireMgr.IgniteAt(_fireMgr.GetClickOnPlane(GetViewport().GetMousePosition()));
        } else if (Input.IsActionPressed("player_leftclick")) { // pour water
            if (_waterGathered > 0) {
                Vector3 fwd = Model.GlobalTransform.Basis.Z;
                Cell cell = _fireMgr.GetCellAt(Position + fwd);
                if (FireManager.CanBecomeWet(cell.State)) {
                    _fireMgr.WetAt(Position + fwd);
                    ModifyWater(-_waterPerUse);
                }
                
            }

        } else if (Input.IsActionJustPressed("player_rightclick")) { // throw water
            if (_waterGathered > 0) {
                Vector3 dir = Model.GlobalTransform.Basis.Z;
                dir = dir.Normalized();
                float spread   = Mathf.DegToRad(_splashSpread);

                int splashNumber = Mathf.CeilToInt(_waterGathered/(_waterPerUse / 2f));
                for (int i = 0; i < splashNumber; i++) {
                    float t     = splashNumber == 1 ? 0.5f : i / (float)(splashNumber - 1);
                    float angle = Mathf.Lerp(-spread * 0.5f, spread * 0.5f, t);
                    Vector3 splashDir = dir.Rotated(Vector3.Up, angle);
                
                    var splash = _waterSplash.Instantiate<WaterSplash>();
                    GetTree().CurrentScene.AddChild(splash);
                    splash.GlobalPosition = GlobalPosition + Vector3.Up * 0.5f;
                    splash.Velocity = (splashDir * _splashSpeed + Vector3.Up * _splashUpSpeed) * (GD.Randf() / 4 + .5f);
                }
                _waterGathered = 0f;

            }
            
        } else if (Input.IsActionPressed("player_gatherwater")) { 
            if (_facingWater) {
                ModifyWater(_waterGatherRate * (float)delta);
            }
        }
        
        DebugUI.Instance.AddLine($"Water gathered: {_waterGathered}/{_maxWater}");
    }
    

    private void ModifyWater(float amt){
        _waterGathered = Mathf.Clamp(_waterGathered + amt, 0.0f, _maxWater);
    }
    
}