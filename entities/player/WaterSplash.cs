using Godot;
using System;
using System.Collections.Generic;

public partial class WaterSplash : Area3D{

	public Vector3 Velocity;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta){
		Velocity += Vector3.Down * 9.81f * 3f * (float)delta;

		Position += Velocity * (float)delta;

		if (Position.Y <= 0f) {
			
			FireManager.Instance.WetAt(Position);
			
			QueueFree();
		}
	}
}
