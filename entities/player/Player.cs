using Godot;

public partial class Player : CharacterBody3D
{
    [Export] public float Speed = 5.0f;
    [Export] public float Acceleration = 40.0f;   // how fast we reach target speed
    [Export] public float RotationSpeed = 12.0f;  // how fast the model turns to face movement

    // Assign the visual "Model" node so we can rotate it without rotating the whole body/camera.
    [Export] public Node3D Model;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 velocity = Velocity;

        // Keep the character grounded (works with or without gravity).
        if (!IsOnFloor())
            velocity += GetGravity() * dt;

        // World-relative input on the XZ plane. Up on the stick = -Z (into the screen).
        Vector2 inputDir = Input.GetVector("player_left", "player_right", "player_forward", "player_backward");
        Vector3 direction = new Vector3(inputDir.X, 0, inputDir.Y).Normalized();

        // Smoothly accelerate horizontal velocity toward the target.
        Vector3 targetVel = direction * Speed;
        velocity.X = Mathf.MoveToward(velocity.X, targetVel.X, Acceleration * dt);
        velocity.Z = Mathf.MoveToward(velocity.Z, targetVel.Z, Acceleration * dt);

        Velocity = velocity;
        MoveAndSlide();

        // Face the direction of movement (only the model, so the camera stays fixed).
        if (Model != null && direction != Vector3.Zero)
        {
            float targetAngle = Mathf.Atan2(direction.X, direction.Z);
            Model.Rotation = new Vector3(
                Model.Rotation.X,
                Mathf.LerpAngle(Model.Rotation.Y, targetAngle, RotationSpeed * dt),
                Model.Rotation.Z);
        }
    }
}