using System.Collections.Generic;
using Godot;
using Kuros.Core;

public partial class FollowingPickupProperty : PickupProperty
{
    public enum AttachmentSlot
    {
        Custom = 0,
        Head = 1,
        Torso = 2,
        LeftHand = 3,
        RightHand = 4
    }

    private static readonly Dictionary<AttachmentSlot, string> SlotPathLookup = new()
    {
        { AttachmentSlot.Head, "Head" },
        { AttachmentSlot.Torso, "Torso" },
        { AttachmentSlot.LeftHand, "LeftHand" },
        { AttachmentSlot.RightHand, "RightHand" }
    };

    [ExportGroup("Bone Binding")]
    [Export(PropertyHint.Enum, "Custom,Head,Torso,LeftHand,RightHand")]
    public AttachmentSlot Slot { get; set; } = AttachmentSlot.Custom;

    [Export] public NodePath SpineAttachmentRootPath = new NodePath("SpineCharacter/Skeleton2D/AttachmentPoints");
    [Export] public NodePath BoneNodePath = new NodePath("SpineCharacter/Skeleton2D/root");
    [Export] public Vector2 BoneLocalOffset = Vector2.Zero;

    protected override void AttachToActor(GameActor actor)
    {
        if (!TryAttachToBone(actor))
        {
            GD.PushWarning($"{Name} 未能绑定到指定部位，退回默认附着逻辑。");
            base.AttachToActor(actor);
            return;
        }

        Position = BoneLocalOffset;
    }

    private bool TryAttachToBone(GameActor actor)
    {
        if (actor == null)
        {
            return false;
        }

        var attachmentPath = ResolveAttachmentPath();
        if (attachmentPath.GetNameCount() == 0)
        {
            return false;
        }

        var targetBoneNode = actor.GetNodeOrNull<Node2D>(attachmentPath);
        if (targetBoneNode == null)
        {
            GD.PrintErr($"{Name} 找不到节点: {attachmentPath}");
            return false;
        }

        var currentParent = GetParent();
        currentParent?.RemoveChild(this);
        targetBoneNode.AddChild(this);

        return true;
    }

    private NodePath ResolveAttachmentPath()
    {
        if (Slot != AttachmentSlot.Custom && SlotPathLookup.TryGetValue(Slot, out var slotName))
        {
            var root = SpineAttachmentRootPath.GetNameCount() > 0 ? SpineAttachmentRootPath.ToString().TrimEnd('/') : string.Empty;
            var combined = string.IsNullOrEmpty(root) ? slotName : $"{root}/{slotName}";
            return new NodePath(combined);
        }

        return BoneNodePath;
    }
}

