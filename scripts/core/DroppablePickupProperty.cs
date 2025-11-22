using Godot;

namespace Kuros.Core
{
    /// <summary>
    ///     Pickup that can be manually put down via the "put_down" action once owned by a player.
    ///     Handles re-parenting back into the world and re-enabling the trigger area for reuse.
    /// </summary>
    public partial class DroppablePickupProperty : PickupProperty
    {
        [ExportGroup("Drop")]
        [Export] public Vector2 DropWorldOffset = new Vector2(0, 20);
        [Export] public bool ReactivateTriggerOnDrop = true;
        [Export] public NodePath DropParentOverride = new NodePath();

        private Node? _dropParentOverride;

        public override void _Ready()
        {
            base._Ready();
            CacheDropParentOverride();
        }

        public override void _Process(double delta)
        {
            if (!IsPicked)
            {
                base._Process(delta);
                return;
            }

            if (OwningActor != null && Input.IsActionJustPressed("put_down"))
            {
                HandlePutDownRequest(OwningActor);
            }
        }

        protected virtual void HandlePutDownRequest(GameActor actor)
        {
            if (!IsPicked || OwningActor != actor)
            {
                return;
            }

            PutDown(actor, DropWorldOffset, ReactivateTriggerOnDrop);
        }

        protected override Node GetDropParent(GameActor actor)
        {
            if (_dropParentOverride != null)
            {
                return _dropParentOverride;
            }

            if (DropParentOverride.GetNameCount() > 0)
            {
                _dropParentOverride = GetNodeOrNull<Node>(DropParentOverride);
                if (_dropParentOverride != null)
                {
                    return _dropParentOverride;
                }
            }

            return base.GetDropParent(actor);
        }

        private void CacheDropParentOverride()
        {
            if (DropParentOverride.GetNameCount() == 0)
            {
                return;
            }

            _dropParentOverride = GetNodeOrNull<Node>(DropParentOverride);
        }
    }
}


