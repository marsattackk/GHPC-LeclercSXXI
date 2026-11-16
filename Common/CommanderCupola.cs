using System;
using System.Linq;
using GHPC.Camera;
using GHPC.Player;
using GHPC.Vehicle;
using UnityEngine;

namespace LeclercSXXI.Common
{
	internal sealed class CommanderCupola : MonoBehaviour
	{
		internal sealed class Settings
		{
			public string Tag;
			public Transform Bone;
			public Vector3 Pivot, Axis;
			public float Speed = 35f;
			public CameraSlot[] Look = new CameraSlot[0];
			public CameraSlot[] Locked = new CameraSlot[0];
			public TargetDesignation.Link Cu;
			// No tank sets Out yet: left null, the cupola follows the commander's look whether he is in or out (CS0649 is off for this declaration only).
#pragma warning disable CS0649
			public Func<bool> Out;
#pragma warning restore CS0649
			public Transform[] Carried = new Transform[0];
		}

		Settings _s;
		Vehicle _vehicle;
		Vector3 _pivot, _axis, _zero, _restPos;
		Quaternion _restRot;
		internal Settings S => _s;
		internal float Yaw { get; private set; }

		internal float Target { get; private set; }

		internal string Following { get; private set; } = "rest";

		internal static CommanderCupola Apply(Vehicle vehicle, Settings s)
		{
			if (vehicle == null || s?.Bone == null || s.Bone.parent == null) return null;
			var root = vehicle.transform;
			var cupola = s.Bone.gameObject.GetComponent<CommanderCupola>() ?? s.Bone.gameObject.AddComponent<CommanderCupola>();
			cupola._s = s;
			cupola._vehicle = vehicle;
			var parent = s.Bone.parent;
			cupola._pivot = parent.InverseTransformPoint(root.TransformPoint(s.Pivot));
			cupola._axis = parent.InverseTransformDirection(root.TransformDirection(s.Axis.normalized)).normalized;
			cupola._zero = Vector3.ProjectOnPlane(parent.InverseTransformDirection(root.forward), cupola._axis).normalized;
			cupola._restPos = s.Bone.localPosition;
			cupola._restRot = s.Bone.localRotation;
			foreach (var part in s.Carried.Where(i => i != null)) part.SetParent(s.Bone, true);
			return cupola;
		}

		float YawOf(Vector3 world)
		{
			var local = Vector3.ProjectOnPlane(_s.Bone.parent.InverseTransformDirection(world), _axis);
			return local.sqrMagnitude < 1e-8f ? Yaw : Vector3.SignedAngle(_zero, local, _axis);
		}

		bool Players()
			=> _vehicle != null
				&& PlayerInput.Instance != null
				&& (object)PlayerInput.Instance.CurrentPlayerUnit == _vehicle;

		void Update()
		{
			if (_s?.Bone == null || _s.Bone.parent == null) return;
			if (Players())
			{
				var view = CameraSlot.ActiveInstance;
				if (view != null && _s.Locked.Contains(view))
				{
					Target = 0f;
					Following = "lined up («Дубль»)";
				}
				else if (_s.Cu != null && _s.Cu.Held)
				{
					Target = YawOf(_s.Cu.Line);
					Following = "the designated line (ЦУ)";
				}
				else if (view != null && _s.Look.Contains(view) && !(_s.Out?.Invoke() ?? false))
				{
					var cam = Camera.main;
					if (cam != null)
					{
						Target = YawOf(cam.transform.forward);
						Following = "the commander's view";
					}
				}
			}
			Yaw = Mathf.MoveTowardsAngle(Yaw, Target, _s.Speed * Time.deltaTime);
			var rotation = Quaternion.AngleAxis(Yaw, _axis);
			_s.Bone.localPosition = _pivot + rotation * (_restPos - _pivot);
			_s.Bone.localRotation = rotation * _restRot;
		}
	}
}
