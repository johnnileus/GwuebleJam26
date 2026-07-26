using Godot;
using System;

public partial class GameOverUi : CanvasLayer
{
	[Export] private Label _label;

	[Export] private Button _continue;
	[Export] private Button _mainMenu;
	
	public override void _Ready(){
		ProcessMode = ProcessModeEnum.Always;

		Visible = false;
		_continue.Pressed += Resume;
		_mainMenu.Pressed += OnMainMenu;
	}

	public void Show(string text){
		Visible = true;
		_label.Text = text;
		_label.Visible = true;
		GetTree().Paused = true; 
	}
	
	private void Resume(){
		GetTree().Paused = false;
		Visible = false;
	}
	private void OnMainMenu(){
		GetTree().Paused = false; 
		GetTree().ChangeSceneToFile("res://scenes/main_menu/main_menu.tscn");
	}
}