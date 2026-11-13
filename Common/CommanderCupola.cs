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
			public Transform Bone;
			public Vector3 Pivot, Axis;
			public float Speed = 35f;
			public CameraSlot[] Look = new CameraSlot[0];
			public CameraSlot[] Locked = new CameraSlot[0];
			public TargetDesignation.Link Cu;
			public Func<bool> Out;
			public Transform[] Carried = new Transform[0];
		}

		Settings _s;
		Vehicle _vehicle;
		Vector3 _pivot, _axis, _zero, _restPos;
		Quaternion _restRot;
		internal float Yaw { get; private set; }

		internal float Target { get; private set; }

		internal static void Apply(Vehicle vehicle, Settings s)
		{
			if (vehicle == null || s?.Bone == null || s.Bone.parent == null) return;
			var root = vehicle.transform;
			var c = s.Bone.gameObject.GetComponent<CommanderCupola>() ?? s.Bone.gameObject.AddComponent<CommanderCupola>();
			c._s = s; c._vehicle = vehicle;
			var p = s.Bone.parent;
			c._pivot = p.InverseTransformPoint(root.TransformPoint(s.Pivot));
			c._axis = p.InverseTransformDirection(root.TransformDirection(s.Axis.normalized)).normalized;
			c._zero = Vector3.ProjectOnPlane(p.InverseTransformDirection(root.forward), c._axis).normalized;
			c._restPos = s.Bone.localPosition;
			c._restRot = s.Bone.localRotation;
			foreach (var t in s.Carried.Where(t => t != null)) t.SetParent(s.Bone, true);
		}

		float YawOf(Vector3 world)
		{
			var d = Vector3.ProjectOnPlane(_s.Bone.parent.InverseTransformDirection(world), _axis);
			return d.sqrMagnitude < 1e-8f ? Yaw : Vector3.SignedAngle(_zero, d, _axis);
		}

		bool Players() => _vehicle != null && PlayerInput.Instance != null && (object)PlayerInput.Instance.CurrentPlayerUnit == _vehicle;

		void Update()
		{
			if (_s?.Bone == null || _s.Bone.parent == null) return;
			if (Players())
			{
				var view = CameraSlot.ActiveInstance;
				if (view != null && _s.Locked.Contains(view)) Target = 0f;
				else if (_s.Cu != null && _s.Cu.Held) Target = YawOf(_s.Cu.Line);
				else if (view != null && _s.Look.Contains(view) && !(_s.Out?.Invoke() ?? false))
				{
					var cam = Camera.main;
					if (cam != null) Target = YawOf(cam.transform.forward);
				}
			}
			Yaw = Mathf.MoveTowardsAngle(Yaw, Target, _s.Speed * Time.deltaTime);
			var q = Quaternion.AngleAxis(Yaw, _axis);
			_s.Bone.localPosition = _pivot + q * (_restPos - _pivot);
			_s.Bone.localRotation = q * _restRot;
		}
	}
}
