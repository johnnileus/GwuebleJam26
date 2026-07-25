using Godot;
using System;

public partial class MainMenu : Control{

	[Export] private string _mainScene;

	[Export] private Button _play;
	[Export] private Button _quit;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_play.Pressed += OnPlayPressed;
		_quit.Pressed += OnQuitPressed;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void OnPlayPressed(){
		GetTree().ChangeSceneToFile(_mainScene);
	}

	private void OnQuitPressed(){
		GetTree().Quit();
	}
}
