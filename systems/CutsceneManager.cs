using Godot;

public partial class CutsceneManager : Node3D
{
    [Export] private Camera3D _cutsceneCamera;
    [Export] private Node3D _chairPile;  
    [Export] private Player _player;

    [Export] private float _panDuration = 3.0f;
    [Export] private CutsceneDialogue _dialogue;

    private static bool _hasPlayed = false;

    public override void _Ready(){
        if (_hasPlayed)
            return;          
        CallDeferred(nameof(StartCutscene));  
    }

    private void StartCutscene(){
        _hasPlayed = true;
        _player.ProcessMode = Node.ProcessModeEnum.Disabled;
        
        Camera3D playerCam = _player.GetNode<Camera3D>("Camera3D");
        _cutsceneCamera.GlobalTransform = playerCam.GlobalTransform;
        _cutsceneCamera.MakeCurrent();
        
        Vector3 target = _chairPile.GlobalPosition + new Vector3(0, 14, 7);
        
        Tween tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        
        tween.TweenCallback(Callable.From(() => _dialogue.Say("Someone left a pile of chairs out here...")));
        tween.TweenProperty(_cutsceneCamera, "global_position", target, 3f);
        tween.TweenInterval(2f);
        
        tween.TweenCallback(Callable.From(IgniteChairs));
        tween.TweenCallback(Callable.From(() => _dialogue.Say("...now they're on fire?")));
        tween.TweenInterval(3f);
        
        tween.TweenCallback(Callable.From(() => _dialogue.Say("Don't let it spread to the forest!!!")));
        tween.TweenInterval(3f);
        
        tween.TweenCallback(Callable.From(() => _dialogue.Clear()));
        tween.TweenCallback(Callable.From(EndCutscene));
    }

    private void IgniteChairs(){
        FireManager.Instance.IgniteAt(_chairPile.GlobalPosition);
    }

    private void EndCutscene(){
        _player.ProcessMode = Node.ProcessModeEnum.Inherit; 
        _player.GetNode<Camera3D>("Camera3D").MakeCurrent();
    }
}