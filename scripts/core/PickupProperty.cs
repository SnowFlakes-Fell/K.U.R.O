using Godot;

namespace Kuros.Core
{
	/// <summary>
	/// 拾取属性基类 - 可以被角色拾取的物品
	/// </summary>
	public abstract partial class PickupProperty : Node2D
	{
		/// <summary>
		/// 标记是否是通过F键手动拾取（用于决定放入左手槽位还是自动分配）
		/// </summary>
		public bool IsManualPickup { get; private set; } = false;
		
		/// <summary>
		/// 标记是否应该直接放入物品栏（当左手已有物品时为 true）
		/// </summary>
		public bool PickupToBackpack { get; private set; } = false;

		/// <summary>
		/// 当被拾取时调用
		/// </summary>
		protected virtual void OnPicked(GameActor actor)
		{
			GD.Print($"{Name} picked up by {actor.Name}");
		}

		/// <summary>
		/// 供外部调用的拾取触发方法（如F键拾取）
		/// </summary>
		/// <param name="actor">拾取的角色</param>
		/// <param name="pickupToBackpack">是否直接放入物品栏（当左手已有物品时为 true）</param>
		public void _TriggerPickup(GameActor actor, bool pickupToBackpack = false)
		{
			if (actor != null)
			{
				IsManualPickup = true;
				PickupToBackpack = pickupToBackpack;
				OnPicked(actor);
				IsManualPickup = false;
				PickupToBackpack = false;
			}
		}
	}
}

