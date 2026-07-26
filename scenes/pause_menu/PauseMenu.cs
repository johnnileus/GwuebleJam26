using Godot;
using System;

public partial class PauseMenu : Control{
	[Export] private Button _continue;
	[Export] private Button _mainMenu;
	[Export] private Button _quit;
	[Export] private string _mainMenuScene;

	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready(){
		ProcessMode = ProcessModeEnum.Always;
		Hide();

		_continue.Pressed += Resume;
		_quit.Pressed += OnQuit;
		_mainMenu.Pressed += OnMainMenu;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("player_pause")) {
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

	private void OnMainMenu(){
		GetTree().Paused = false; 
		GetTree().ChangeSceneToFile(_mainMenuScene);
	}
	
	
	
}
