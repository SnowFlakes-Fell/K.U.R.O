using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Items.Effects;
using Kuros.Items;
using Kuros.Managers;
using Kuros.Systems.Inventory;
using Kuros.Utils;

namespace Kuros.Items.World
{
    /// <summary>
    /// 地图场景中的物品实体，与具体 <see cref="ItemDefinition"/> 一一对应，
    /// 负责识别物品、暴露属性并在拾取/丢弃时与背包系统交互。
    /// </summary>
    public partial class WorldItemEntity : CharacterBody2D
    {
        [Signal] public delegate void ItemTransferredEventHandler(WorldItemEntity entity, GameActor actor, ItemDefinition item, int amount);
        [Signal] public delegate void ItemTransferFailedEventHandler(WorldItemEntity entity, GameActor actor);

        [ExportGroup("Item")]
        [Export] public ItemDefinition? ItemDefinition { get; set; }
        [Export(PropertyHint.File, "*.tres,*.res")] public string ItemDefinitionResourcePath { get; set; } = string.Empty;
        [Export] public string ItemIdOverride { get; set; } = string.Empty;
        [Export(PropertyHint.Range, "1,9999,1")] public int Quantity { get; set; } = 1;

        [ExportGroup("Pickup")]
        [Export] public Area2D TriggerArea { get; private set; } = null!;
        [Export] public bool AutoDisableTriggerOnPickup { get; set; } = true;

        [ExportGroup("World Physics")]
        [Export(PropertyHint.Range, "0,4000,1")] public float ThrowFriction = 600f;
        [Export] public uint BodyCollisionLayer { get; set; } = 1u << 2;
        [Export] public uint BodyCollisionMask { get; set; } = 1u;
        [Export] public uint TriggerCollisionLayer { get; set; } = 1u << 1;
        [Export] public uint TriggerCollisionMask { get; set; } = 1u;

        public InventoryItemStack? CurrentStack { get; private set; }
        public string ItemId => !string.IsNullOrWhiteSpace(ItemIdOverride)
            ? ItemIdOverride
            : ItemDefinition?.ItemId ?? DeriveItemIdFromScene();

        private ItemDefinition? _lastTransferredItem;
        private int _lastTransferredAmount;
        private Vector2 _pendingVelocity;
        private GameActor? _focusedActor;
        private bool _isPicked;
        private bool _initialMonitoring;
        private bool _initialMonitorable;
        private uint _initialCollisionLayer;
        private uint _initialCollisionMask;

        public override void _Ready()
        {
            base._Ready();
            InitializeStack();
            ResolveTriggerArea();
            ApplyCollisionSettings();
            SetProcess(true);
            SetPhysicsProcess(true);
        }

        public override void _ExitTree()
        {
            base._ExitTree();
            if (TriggerArea != null)
            {
                TriggerArea.BodyEntered -= OnBodyEntered;
                TriggerArea.BodyExited -= OnBodyExited;
            }
        }

        public override void _Process(double delta)
        {
            base._Process(delta);

            if (_isPicked || _focusedActor == null)
            {
                return;
            }

            if (!GodotObject.IsInstanceValid(_focusedActor))
            {
                _focusedActor = null;
                return;
            }

            // 检查对话是否激活，如果激活则不处理拾取（让NPC交互优先）
            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive)
            {
                return;
            }

            if (Input.IsActionJustPressed("take_up"))
            {
                HandlePickupRequest(_focusedActor);
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            base._PhysicsProcess(delta);

            if (_pendingVelocity.LengthSquared() > 0.0001f)
            {
                Velocity = _pendingVelocity;
                MoveAndSlide();
                float decel = ThrowFriction * (float)delta;
                _pendingVelocity = _pendingVelocity.MoveToward(Vector2.Zero, decel);
                if (_pendingVelocity.LengthSquared() <= 0.0001f)
                {
                    Velocity = Vector2.Zero;
                }
            }
            else if (Velocity.LengthSquared() > 0.0001f)
            {
                Velocity = Vector2.Zero;
            }
        }

        public Dictionary<string, float> GetAttributeSnapshot()
        {
            if (CurrentStack != null)
            {
                return CurrentStack.Item.GetAttributeSnapshot(CurrentStack.Quantity);
            }

            return ItemDefinition != null
                ? ItemDefinition.GetAttributeSnapshot(Math.Max(1, Quantity))
                : new Dictionary<string, float>();
        }

        public void InitializeFromStack(InventoryItemStack stack)
        {
            if (stack == null) throw new ArgumentNullException(nameof(stack));

            ItemDefinition = stack.Item;
            Quantity = stack.Quantity;
            CurrentStack = new InventoryItemStack(stack.Item, stack.Quantity);
            
            // 更新 Sprite 貼圖為物品圖標
            UpdateSpriteFromItemDefinition();
        }

        public void InitializeFromItem(ItemDefinition definition, int quantity)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            quantity = Math.Max(1, quantity);

            ItemDefinition = definition;
            Quantity = quantity;
            CurrentStack = new InventoryItemStack(definition, quantity);
            
            // 更新 Sprite 貼圖為物品圖標
            UpdateSpriteFromItemDefinition();
        }

        /// <summary>
        /// 更新 Sprite2D 的貼圖為當前物品的圖標
        /// </summary>
        private void UpdateSpriteFromItemDefinition()
        {
            if (ItemDefinition?.Icon == null)
            {
                return;
            }

            var sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
            if (sprite != null)
            {
                sprite.Texture = ItemDefinition.Icon;
                // 重置縮放以確保圖標以原始大小顯示
                sprite.Scale = Vector2.One;
            }
        }

        public void ApplyThrowImpulse(Vector2 velocity)
        {
            _pendingVelocity = velocity;
            Velocity = velocity;
        }

        /// <summary>
        /// 供外部调用的拾取方法（如状态机或其他组件触发）
        /// </summary>
        /// <param name="actor">拾取物品的角色</param>
        /// <param name="pickupToBackpack">如果为 true，物品将直接放入物品栏而非左手</param>
        public bool TryPickupByActor(GameActor actor, bool pickupToBackpack = false)
        {
            if (_isPicked)
            {
                return false;
            }

            if (!TryTransferToActor(actor, pickupToBackpack))
            {
                return false;
            }

            ApplyItemEffects(actor, ItemEffectTrigger.OnPickup);

            // 检查是否为部分转移（地面仍有剩余物品）
            if (Quantity > 0)
            {
                // 部分转移 - 发出信号但保持实体可交互
                if (_lastTransferredItem != null && _lastTransferredAmount > 0)
                {
                    EmitSignal(SignalName.ItemTransferred, this, actor, _lastTransferredItem, _lastTransferredAmount);
                }
                return true;
            }

            // 完整转移 - 继续正常的拾取完成流程
            _isPicked = true;
            if (AutoDisableTriggerOnPickup)
            {
                DisableTriggerArea();
            }

            OnPicked(actor);
            return true;
        }

        private void ResolveTriggerArea()
        {
            if (TriggerArea == null)
            {
                TriggerArea = GetNodeOrNull<Area2D>("TriggerArea") ??
                              throw new InvalidOperationException($"{Name} 缺少 TriggerArea 节点。");
            }

            _initialMonitoring = TriggerArea.Monitoring;
            _initialMonitorable = TriggerArea.Monitorable;
            TriggerArea.BodyEntered += OnBodyEntered;
            TriggerArea.BodyExited += OnBodyExited;

            TriggerArea.CollisionLayer = TriggerCollisionLayer;
            TriggerArea.CollisionMask = TriggerCollisionMask;
            _initialCollisionLayer = TriggerArea.CollisionLayer;
            _initialCollisionMask = TriggerArea.CollisionMask;
        }

        private void ApplyCollisionSettings()
        {
            CollisionLayer = BodyCollisionLayer;
            CollisionMask = BodyCollisionMask;
        }

        private void OnBodyEntered(Node2D body)
        {
            if (body is GameActor actor)
            {
                _focusedActor = actor;
            }
        }

        private void OnBodyExited(Node2D body)
        {
            if (_focusedActor == null)
            {
                return;
            }

            if (body == _focusedActor)
            {
                _focusedActor = null;
            }
        }

        private void HandlePickupRequest(GameActor actor)
        {
            // 检查左手是否有物品，决定是否放入物品栏
            bool pickupToBackpack = false;
            if (actor is SamplePlayer player && player.LeftHandSlotIndex >= 1 && player.LeftHandSlotIndex <= 4)
            {
                var inventory = ResolveInventoryComponent(actor);
                if (inventory?.QuickBar != null)
                {
                    var stack = inventory.QuickBar.GetStack(player.LeftHandSlotIndex);
                    // 检查槽位是否有有效物品（排除空白道具）
                    if (stack != null && !stack.IsEmpty && stack.Item.ItemId != "empty_item")
                    {
                        pickupToBackpack = true;
                        GD.Print("[WorldItemEntity] 左手已有物品，将拾取到物品栏");
                    }
                }
            }
            
            if (!TryPickupByActor(actor, pickupToBackpack))
            {
                EmitSignal(SignalName.ItemTransferFailed, this, actor);
            }
        }

        private void DisableTriggerArea()
        {
            if (TriggerArea == null)
            {
                return;
            }

            TriggerArea.Monitoring = false;
            TriggerArea.Monitorable = false;
            TriggerArea.CollisionLayer = 0;
            TriggerArea.CollisionMask = 0;
        }

        private void RestoreTriggerArea()
        {
            if (TriggerArea == null)
            {
                return;
            }

            TriggerArea.Monitoring = _initialMonitoring;
            TriggerArea.Monitorable = _initialMonitorable;
            TriggerArea.CollisionLayer = _initialCollisionLayer;
            TriggerArea.CollisionMask = _initialCollisionMask;
        }

        private void OnPicked(GameActor actor)
        {
            if (_lastTransferredItem != null && _lastTransferredAmount > 0)
            {
                EmitSignal(SignalName.ItemTransferred, this, actor, _lastTransferredItem, _lastTransferredAmount);
            }

            QueueFree();
        }

        private void InitializeStack()
        {
            var definition = ResolveItemDefinition();
            if (definition == null)
            {
                GameLogger.Error(nameof(WorldItemEntity), $"{Name} 无法解析物品定义，路径：{ItemDefinitionResourcePath}, 推断 Id：{ItemId}");
                QueueFree();
                return;
            }

            Quantity = Math.Max(1, Quantity);
            CurrentStack = new InventoryItemStack(definition, Quantity);
        }

        private bool TryTransferToActor(GameActor actor, bool pickupToBackpack = false)
        {
            var stack = CurrentStack;
            if (stack == null)
            {
                return false;
            }

            var inventory = ResolveInventoryComponent(actor);
            if (inventory == null)
            {
                GameLogger.Warn(nameof(WorldItemEntity), $"Actor {actor.Name} 缺少 PlayerInventoryComponent，无法拾取 {ItemId}。");
                return false;
            }

            int accepted = 0;
            
            // 如果指定直接放入物品栏，使用 AddItemSmart 方法
            if (pickupToBackpack)
            {
                GD.Print($"[拾取] 左手已有物品，将 {stack.Item.DisplayName} 放入物品栏");
                accepted = inventory.AddItemSmart(stack.Item, stack.Quantity, showPopupIfFirstTime: true);
                if (accepted > 0)
                {
                    GD.Print($"[拾取] {accepted} x {ItemId} -> 快捷栏/物品栏");
                }
            }
            else
            {
                // 优先将物品添加到快捷栏的左手槽位
                if (actor is SamplePlayer player && player.LeftHandSlotIndex >= 1 && player.LeftHandSlotIndex <= 4 && inventory.QuickBar != null)
                {
                    // 尝试将物品添加到左手选中的快捷栏槽位
                    accepted = inventory.QuickBar.TryAddItemToSlot(stack.Item, stack.Quantity, player.LeftHandSlotIndex);
                    if (accepted > 0)
                    {
                        GD.Print($"[拾取] {accepted} x {ItemId} -> 左手槽位 {player.LeftHandSlotIndex + 1}");
                        GameLogger.Info(nameof(WorldItemEntity), $"{actor.Name} 将 {accepted} 个 {ItemId} 拾取到左手快捷栏槽位 {player.LeftHandSlotIndex + 1}。");
                        inventory.NotifyItemPicked(stack.Item);
                        // 同步更新左手物品显示
                        player.SyncLeftHandItemFromSlot();
                        player.UpdateHandItemVisual();
                    }
                }
                
                // 如果左手槽位无法接收（或未设置），使用 AddItemSmart 智能分配
                if (accepted <= 0)
                {
                    GD.Print($"[拾取] 左手槽位无法接收，使用智能分配");
                    accepted = inventory.AddItemSmart(stack.Item, stack.Quantity, showPopupIfFirstTime: true);
                    if (accepted <= 0)
                    {
                        GameLogger.Info(nameof(WorldItemEntity), $"Actor {actor.Name} 无法拾取 {ItemId}，所有槽位都已满。");
                        return false;
                    }
                    GD.Print($"[拾取] {accepted} x {ItemId} -> 快捷栏/物品栏");
                }
            }

            if (accepted <= 0)
            {
                GameLogger.Info(nameof(WorldItemEntity), $"Actor {actor.Name} 无法拾取 {ItemId}，物品栏可能已满。");
                return false;
            }

            if (accepted < stack.Quantity)
            {
                stack.Remove(accepted);
                Quantity = stack.Quantity;
                _lastTransferredItem = stack.Item;
                _lastTransferredAmount = accepted;
                GameLogger.Info(nameof(WorldItemEntity), $"{actor.Name} 仅拾取了 {accepted} 个 {ItemId}，剩余 {Quantity} 个保留在地面。");
                RestoreTriggerArea();
                _isPicked = false;
                return true;
            }

            _lastTransferredItem = stack.Item;
            _lastTransferredAmount = accepted;
            CurrentStack = null;
            Quantity = 0;
            return true;
        }

        private ItemDefinition? ResolveItemDefinition()
        {
            if (ItemDefinition != null)
            {
                return ItemDefinition;
            }

            if (!string.IsNullOrWhiteSpace(ItemDefinitionResourcePath))
            {
                var loaded = ResourceLoader.Load<ItemDefinition>(ItemDefinitionResourcePath);
                if (loaded != null)
                {
                    ItemDefinition = loaded;
                    return ItemDefinition;
                }
            }

            return ItemDefinition;
        }

        private string DeriveItemIdFromScene()
        {
            if (!string.IsNullOrEmpty(SceneFilePath))
            {
                return Path.GetFileNameWithoutExtension(SceneFilePath);
            }

            return Name;
        }

        private static PlayerInventoryComponent? ResolveInventoryComponent(GameActor actor)
        {
            if (actor == null)
            {
                return null;
            }

            if (actor is SamplePlayer samplePlayer && samplePlayer.InventoryComponent != null)
            {
                return samplePlayer.InventoryComponent;
            }

            var direct = actor.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            if (direct != null)
            {
                return direct;
            }

            return FindChildComponent<PlayerInventoryComponent>(actor);
        }

        private void ApplyItemEffects(GameActor actor, ItemEffectTrigger trigger)
        {
            if (actor?.EffectController == null)
            {
                return;
            }

            if (ItemDefinition == null)
            {
                return;
            }

            foreach (var effectEntry in ItemDefinition.GetEffectEntries(trigger))
            {
                var effect = effectEntry.InstantiateEffect();
                if (effect == null) continue;
                actor.ApplyEffect(effect);
            }
        }

        private static T? FindChildComponent<T>(Node root) where T : Node
        {
            foreach (Node child in root.GetChildren())
            {
                if (child is T typed)
                {
                    return typed;
                }

                if (child.GetChildCount() > 0)
                {
                    var nested = FindChildComponent<T>(child);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }
    }
}
