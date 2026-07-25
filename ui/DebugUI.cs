using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class DebugUI : CanvasLayer{

	public static DebugUI Instance { get; private set; }

	
	[Export] private Label _label;
	private List<string> _texts = new List<string>();

	public override void _Ready(){
		Instance = this;
		
	}

	public override void _Process(double delta){
		AddLine($"FPS: {Engine.GetFramesPerSecond()}");

		string output = "";
		foreach (var text in _texts) {
			output += text + '\n';
		}

		_label.Text = output;
		_texts.Clear();
	}

	public void AddLine(string str){
		_texts.Add(str);
	}
}
