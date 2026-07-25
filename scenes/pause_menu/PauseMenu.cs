using Godot;
using System;

public partial class PauseMenu : Control{
	[Export] private Button _continue;
	[Export] private Button _quit;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready(){
		ProcessMode = ProcessModeEnum.Always;
		Hide();

		_continue.Pressed += Resume;
		_quit.Pressed += OnQuit;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("player_pause"))
		{
			if (GetTree().Paused) Resume();
			else Pause();
		}
	}

	private void Pause(){
		GetTree().Paused = true;
		Show();
	}
	
	private void Resume(){
		GetTree().Paused = false;
		Hide();
	}
	
	private void OnQuit(){
		GetTree().Paused = false; 
		GetTree().Quit(); 
	}
	
	
	
}
