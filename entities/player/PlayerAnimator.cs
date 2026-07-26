using Godot;
using System;

public partial class PlayerAnimator : AnimationPlayer
{
    public static PlayerAnimator Instance;
    public static void Walk()
    {
        Instance.Play("walk");
    }

    public static void Idle()
    {
        Instance.Stop();
    }

    public static void GatherWalter()
    {
        Instance.Play("gather_water");
    }

    public override void _Ready()
    {
        Instance = this;
    }
}
