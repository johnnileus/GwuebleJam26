using Godot;

public partial class CutsceneDialogue : CanvasLayer
{
    [Export] private Label _label;
    [Export] private float _charsPerSecond = 30f;

    private Tween _typeTween;

    public override void _Ready(){
        _label.Text = "";
        Visible = false;
    }

    // call this to show a line with a typewriter reveal
    public void Say(string line){
        Visible = true;
        _label.Text = line;
        _label.VisibleRatio = 0f; 

        _typeTween?.Kill();
        _typeTween = CreateTween();
        float duration = line.Length / _charsPerSecond;
        _typeTween.TweenProperty(_label, "visible_ratio", 1f, duration);
    }

    public void Clear(){
        _typeTween?.Kill();
        _label.Text = "";
        Visible = false;
    }
}