using Godot;
using Kuros.Core;

public partial class ExampleProperty : PickupProperty
{
    [Export] public int ScoreBonus = 10;

    protected override void OnPicked(GameActor actor)
    {
        base.OnPicked(actor);

        if (actor is SamplePlayer player)
        {
            player.AddScore(ScoreBonus);
        }
    }
}

